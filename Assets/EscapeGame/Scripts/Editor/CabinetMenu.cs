using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game > Rendre l'armoire ouvrable" pour le modèle cabinet00 (Assets/3D/Armoire) :
    /// referme la porte modélisée entrouverte, attache chaque poignée à sa porte, pose une charnière (HingedDoor)
    /// sur le bord extérieur de chaque porte et donne à l'armoire des collisions à sa forme exacte (intérieur accessible).
    /// </summary>
    static class CabinetMenu
    {
        // Noms des pièces dans cabinet00.dae (export SketchUp) : portes et poignées.
        const string k_DoorBottomLeft = "instance_29";
        const string k_DoorBottomRight = "instance_30";
        const string k_DoorTopLeft = "instance_31";
        const string k_DoorTopRight = "instance_27"; // modélisée ouverte à 45°
        static readonly Dictionary<string, string> k_Handles = new Dictionary<string, string>
        {
            { "instance_32", k_DoorTopLeft },
            { "instance_33", k_DoorBottomLeft },
            { "instance_34", k_DoorBottomRight },
            // La poignée de la porte en haut à droite est déjà son enfant.
        };
        const float k_MinColliderSize = 0.1f; // les petites pièces (charnières, vis) n'ont pas besoin de collision

        [MenuItem("GameObject/Escape Game/Rendre l'armoire ouvrable", false, 17)]
        static void MakeCabinetOpenable(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            var cabinet = selected;
            while (cabinet != null && FindDeep(cabinet.transform, k_DoorTopLeft) == null)
                cabinet = cabinet.transform.parent != null ? cabinet.transform.parent.gameObject : null;
            if (cabinet == null)
            {
                EditorUtility.DisplayDialog("Armoire", "Sélectionne l'armoire (modèle cabinet00) dans la Hierarchy.", "OK");
                return;
            }
            if (cabinet.GetComponentInChildren<HingedDoor>() != null)
            {
                EditorUtility.DisplayDialog("Armoire", "Cette armoire est déjà ouvrable.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Rendre l'armoire ouvrable");
            var group = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            // Les pièces d'un modèle importé ne peuvent pas changer de parent : on "déballe" l'instance.
            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(cabinet);
            if (prefabRoot != null)
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.UserAction);

            var root = cabinet.transform;
            var doors = new[] { k_DoorTopLeft, k_DoorTopRight, k_DoorBottomLeft, k_DoorBottomRight }
                .Select(n => FindDeep(root, n)).ToArray();
            if (doors.Any(d => d == null))
            {
                EditorUtility.DisplayDialog("Armoire", "Portes introuvables : ce menu est prévu pour le modèle cabinet00.", "OK");
                return;
            }

            CloseTopRightDoor(doors[0], doors[1], doors[2], doors[3]);
            report.AppendLine("• Porte du haut à droite refermée (elle était modélisée entrouverte)");

            // Repère de l'armoire, en mètres : vertical du monde, axes horizontaux alignés sur l'armoire
            // (le modèle SketchUp est importé tourné, son "haut" d'origine n'est pas forcément le Y de son transform).
            var horizontal = new[] { root.right, root.up, root.forward }
                .OrderBy(a => Mathf.Abs(Vector3.Dot(a, Vector3.up))).First();
            var frameRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(horizontal, Vector3.up).normalized, Vector3.up);
            var frame = Matrix4x4.TRS(root.position, frameRotation, Vector3.one);
            var toFrame = frame.inverse;
            var cabinetBounds = BoundsIn(root, toFrame);

            var doorSet = new HashSet<Transform>(doors);
            var pivots = new List<Transform>();
            foreach (var door in doors)
            {
                var handles = k_Handles.Where(h => h.Value == door.name).Select(h => FindDeep(root, h.Key)).Where(h => h != null).ToList();
                pivots.Add(CreateHinge(root, door, handles, frame, toFrame, cabinetBounds));
            }
            report.AppendLine($"• {pivots.Count} portes à charnière : on les attrape et on les fait pivoter");

            var colliders = ReplaceColliders(root, pivots);
            report.AppendLine($"• Collisions à la forme exacte de l'armoire ({colliders} pièces) : on peut poser des objets à l'intérieur");

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = cabinet;
            EditorUtility.DisplayDialog("Armoire", report +
                "\nRelance ensuite Generate Lighting (les portes ne sont plus Static), puis Ctrl+S.", "OK");
        }

        [MenuItem("GameObject/Escape Game/Rendre l'armoire ouvrable", true)]
        static bool Validate() => Selection.activeGameObject != null;

        // Même orientation que la porte du haut à gauche, à la place symétrique de celle du bas à droite.
        static void CloseTopRightDoor(Transform topLeft, Transform topRight, Transform bottomLeft, Transform bottomRight)
        {
            if (topRight.parent != topLeft.parent || bottomLeft.parent != topLeft.parent || bottomRight.parent != topLeft.parent)
                return;
            Undo.RecordObject(topRight, "Fermer la porte");
            topRight.localRotation = topLeft.localRotation;
            topRight.localPosition = bottomRight.localPosition + (topLeft.localPosition - bottomLeft.localPosition);
        }

        static Transform CreateHinge(Transform root, Transform door, List<Transform> handles, Matrix4x4 frame, Matrix4x4 toFrame, Bounds cabinet)
        {
            var b = BoundsIn(door, toFrame);

            // Épaisseur de la porte : la plus petite dimension horizontale. Largeur : l'autre.
            var thinIsX = b.size.x < b.size.z;
            var thin = thinIsX ? Vector3.right : Vector3.forward;
            var width = thinIsX ? Vector3.forward : Vector3.right;
            // Face avant : du côté opposé au centre de l'armoire. Charnière : bord extérieur (loin du centre).
            var frontSign = Mathf.Sign(Vector3.Dot(b.center - cabinet.center, thin));
            var outerSign = Mathf.Sign(Vector3.Dot(b.center - cabinet.center, width));
            var halfThin = Vector3.Dot(b.extents, thin);
            var halfWidth = Vector3.Dot(b.extents, width);

            var hinge = b.center + width * (outerSign * halfWidth) + thin * (frontSign * halfThin);
            var freeEdge = b.center - width * (outerSign * halfWidth) + thin * (frontSign * halfThin);

            var pivot = new GameObject("Charnière " + door.name).transform;
            Undo.RegisterCreatedObjectUndo(pivot.gameObject, "Charnière");
            pivot.SetParent(door.parent, false);
            pivot.SetPositionAndRotation(frame.MultiplyPoint3x4(hinge), frame.rotation);
            var ps = pivot.parent != null ? pivot.parent.lossyScale : Vector3.one;
            pivot.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);

            Undo.SetTransformParent(door, pivot, "Charnière");
            foreach (var handle in handles)
                Undo.SetTransformParent(handle, pivot, "Charnière");
            ClearStatic(pivot);

            // Sens d'ouverture : le bord libre doit partir vers l'avant de l'armoire.
            var up = frame.MultiplyVector(Vector3.up);
            var r = frame.MultiplyVector(freeEdge - hinge);
            var front = frame.MultiplyVector(thin * frontSign);
            var openSign = Mathf.Sign(Vector3.Dot(Vector3.Cross(up, r), front));

            // Collision de la porte (un peu plus épaisse vers l'avant, pour être visée avant le corps de l'armoire).
            var colliderGo = new GameObject("Collision porte");
            colliderGo.transform.SetParent(pivot, false);
            var box = colliderGo.AddComponent<BoxCollider>();
            box.center = b.center - hinge + thin * (frontSign * 0.005f);
            box.size = b.size + thin * 0.01f;

            var body = Undo.AddComponent<Rigidbody>(pivot.gameObject);
            body.isKinematic = true;
            body.useGravity = false;

            var interactable = Undo.AddComponent<XRSimpleInteractable>(pivot.gameObject);
            interactable.colliders.Clear();
            interactable.colliders.Add(box);

            var hingedDoor = Undo.AddComponent<HingedDoor>(pivot.gameObject);
            hingedDoor.openSign = openSign;
            EditorUtility.SetDirty(interactable);
            return pivot;
        }

        // La boîte de collision éventuelle de l'armoire remplirait l'intérieur : on la remplace par sa forme exacte.
        static int ReplaceColliders(Transform root, List<Transform> pivots)
        {
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger || !box.enabled || pivots.Any(p => box.transform.IsChildOf(p)))
                    continue;
                Undo.RecordObject(box, "Collision armoire");
                box.enabled = false;
            }

            var count = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || pivots.Any(p => filter.transform.IsChildOf(p)) ||
                    filter.GetComponent<Collider>() != null)
                    continue;
                var size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
                if (Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) < k_MinColliderSize)
                    continue;
                Undo.AddComponent<MeshCollider>(filter.gameObject).sharedMesh = filter.sharedMesh;
                count++;
            }
            return count;
        }

        static void ClearStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0)
                    continue;
                Undo.RecordObject(t.gameObject, "Porte non statique");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        // Boîte englobante des meshes sous "t", dans le repère "toFrame" (armoire, en mètres).
        static Bounds BoundsIn(Transform t, Matrix4x4 toFrame)
        {
            var has = false;
            var result = new Bounds();
            foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var local = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toFrame.MultiplyPoint3x4(filter.transform.TransformPoint(corner));
                    if (has)
                        result.Encapsulate(p);
                    else
                        result = new Bounds(p, Vector3.zero);
                    has = true;
                }
            }
            return result;
        }
    }
}
