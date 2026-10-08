using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game > Rendre les tiroirs ouvrables" : sur un bureau sélectionné, trouve les tiroirs
    /// (objets "...DrawerShelfBone..."), les rend ouvrables à la main, verrouille celui du haut avec un cadenas
    /// et pose sa clé sur le bureau, et laisse celui du bas entrouvert.
    /// </summary>
    static class DrawerMenu
    {
        const string k_DrawerNameTag = "DrawerShelfBone";
        const float k_Wall = 0.015f;          // épaisseur (m) des collisions du tiroir
        const float k_OpenRatio = 0.6f;       // ouverture maximale = 60 % de la profondeur
        const float k_BottomStartOpen = 0.12f; // tiroir du bas entrouvert (m)

        [MenuItem("GameObject/Escape Game/Rendre les tiroirs ouvrables", false, 14)]
        static void MakeDrawersOpenable(MenuCommand command)
        {
            var desk = command.context as GameObject ?? Selection.activeGameObject;
            if (desk == null)
                return;
            // On remonte jusqu'au meuble qui contient les tiroirs (clic possible sur un tiroir ou une pièce du bureau).
            while (desk.transform.parent != null && !HasDrawers(desk.transform))
                desk = desk.transform.parent.gameObject;

            var frame = desk.transform;
            var drawerRoots = frame.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.Contains(k_DrawerNameTag) && (t.parent == null || !t.parent.name.Contains(k_DrawerNameTag)))
                .ToList();
            if (drawerRoots.Count == 0)
            {
                EditorUtility.DisplayDialog("Tiroirs", $"Aucun tiroir trouvé dans « {desk.name} » (objets nommés « …{k_DrawerNameTag}… »).", "OK");
                return;
            }

            var deskMesh = FindDeskMesh(frame, drawerRoots);
            if (deskMesh == null)
            {
                EditorUtility.DisplayDialog("Tiroirs", "Mesh du bureau introuvable.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Rendre les tiroirs ouvrables");
            var group = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            // Repère du meuble, en mètres (sans l'échelle d'import du modèle).
            var space = Matrix4x4.TRS(frame.position, frame.rotation, Vector3.one);
            var toSpace = space.inverse;
            var deskBounds = MeshBoundsIn(deskMesh.transform, deskMesh.sharedMesh.bounds, toSpace);

            ReplaceDeskCollider(desk, deskMesh, report);

            // Du plus haut au plus bas.
            var unstatic = 0;
            var drawers = new List<Drawer>();
            foreach (var root in drawerRoots)
            {
                unstatic += ClearStatic(root);
                if (root.GetComponent<Drawer>() != null)
                {
                    drawers.Add(root.GetComponent<Drawer>());
                    continue;
                }
                var bounds = DrawerBoundsIn(root, toSpace);
                if (bounds.size == Vector3.zero)
                    continue;
                drawers.Add(SetupDrawer(root, frame, space, deskBounds, bounds));
            }
            drawers = drawers.OrderByDescending(d => d.transform.position.y).ToList();
            if (unstatic > 0)
                report.AppendLine($"• {unstatic} pièce(s) des tiroirs n'est plus Static (un objet Static ne bouge pas à l'écran) : refais Generate Lighting");
            report.AppendLine($"• {drawers.Count} tiroir(s) ouvrables à la main");

            if (drawers.Count > 0)
            {
                var bottom = drawers[drawers.Count - 1];
                Undo.RecordObject(bottom, "Tiroir entrouvert");
                bottom.startOpen = Mathf.Min(k_BottomStartOpen, bottom.maxOpen);
                report.AppendLine($"• « {bottom.name} » (en bas) entrouvert au lancement");
            }

            if (drawers.Count > 1 && drawers[0].GetComponentInChildren<KeyLock>() == null)
            {
                var top = drawers[0];
                Undo.RecordObject(top, "Tiroir verrouillé");
                top.locked = true;
                var keyLock = CreatePadlock(top, frame);
                var key = CreateKey(frame, deskBounds, space, top);
                report.AppendLine($"• « {top.name} » (en haut) verrouillé par un cadenas");
                report.AppendLine($"• Clé posée sur le bureau : déplace « {key.name} » pour la cacher");
                Selection.activeGameObject = keyLock.gameObject;
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Tiroirs de « {desk.name} » :\n{report}");
            EditorUtility.DisplayDialog("Tiroirs", report + "\nPense à faire Ctrl+S.", "OK");
        }

        // Un objet Static est fusionné avec le décor (static batching) : il ne bouge plus à l'écran même si sa position change.
        static int ClearStatic(Transform root)
        {
            var count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0)
                    continue;
                Undo.RecordObject(t.gameObject, "Tiroir non statique");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                count++;
            }
            return count;
        }

        static bool HasDrawers(Transform t) =>
            t.GetComponentsInChildren<Transform>(true).Any(c => c != t && c.name.Contains(k_DrawerNameTag));

        [MenuItem("GameObject/Escape Game/Rendre les tiroirs ouvrables", true)]
        static bool Validate() => Selection.activeGameObject != null;

        // ---- Bureau ----

        static MeshFilter FindDeskMesh(Transform frame, List<Transform> drawerRoots)
        {
            return frame.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && !drawerRoots.Any(d => f.transform.IsChildOf(d)))
                .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude)
                .FirstOrDefault();
        }

        // La grande boîte de collision du bureau remplit les tiroirs : on la remplace par la forme exacte du meuble.
        static void ReplaceDeskCollider(GameObject desk, MeshFilter deskMesh, StringBuilder report)
        {
            var disabled = 0;
            foreach (var box in desk.GetComponents<BoxCollider>())
            {
                if (!box.enabled || box.isTrigger)
                    continue;
                Undo.RecordObject(box, "Collision bureau");
                box.enabled = false;
                disabled++;
            }

            if (deskMesh.GetComponent<MeshCollider>() == null)
            {
                var meshCollider = Undo.AddComponent<MeshCollider>(deskMesh.gameObject);
                meshCollider.sharedMesh = deskMesh.sharedMesh;
                report.AppendLine(disabled > 0
                    ? "• Boîte de collision du bureau remplacée par sa forme exacte (pour pouvoir mettre des objets dans les tiroirs)"
                    : "• Collision à la forme exacte du bureau ajoutée");
            }
        }

        // ---- Tiroirs ----

        static Drawer SetupDrawer(Transform root, Transform frame, Matrix4x4 space, Bounds deskBounds, Bounds bounds)
        {
            // Face avant = face du tiroir la plus proche du bord du bureau (côté où il dépasse).
            var gaps = new[]
            {
                (axis: Vector3.right, gap: deskBounds.max.x - bounds.max.x, size: bounds.size.x),
                (axis: Vector3.left, gap: bounds.min.x - deskBounds.min.x, size: bounds.size.x),
                (axis: Vector3.forward, gap: deskBounds.max.z - bounds.max.z, size: bounds.size.z),
                (axis: Vector3.back, gap: bounds.min.z - deskBounds.min.z, size: bounds.size.z),
            };
            var front = gaps.OrderBy(g => g.gap).First();
            var axis = front.axis;              // direction d'ouverture, repère du meuble
            var depth = front.size;
            var side = Vector3.Cross(Vector3.up, axis);
            var width = Mathf.Abs(Vector3.Dot(bounds.size, side));
            var height = bounds.size.y;

            // Collisions, dans un repère aligné sur le meuble et à l'échelle 1 (en mètres).
            var collisions = new GameObject("Collisions tiroir").transform;
            Undo.RegisterCreatedObjectUndo(collisions.gameObject, "Collisions tiroir");
            collisions.SetParent(root, false);
            collisions.SetPositionAndRotation(space.MultiplyPoint3x4(bounds.center), frame.rotation);
            var ls = root.lossyScale;
            collisions.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);

            Box Make(string name, Vector3 center, float alongAxis, float alongSide, float alongUp) =>
                new Box(collisions, name, center, axis * alongAxis + side * alongSide + Vector3.up * alongUp);

            var wallHeight = height * 0.8f;
            var wallY = -height / 2f + wallHeight / 2f;
            var frontBox = Make("Façade", axis * (depth / 2f - k_Wall / 2f), k_Wall, width, height).collider;
            Make("Fond", Vector3.up * (-height / 2f + k_Wall / 2f), depth, width, k_Wall);
            Make("Arrière", -axis * (depth / 2f - k_Wall / 2f) + Vector3.up * wallY, k_Wall, width, wallHeight);
            Make("Côté 1", side * (width / 2f - k_Wall / 2f) + Vector3.up * wallY, depth, k_Wall, wallHeight);
            Make("Côté 2", -side * (width / 2f - k_Wall / 2f) + Vector3.up * wallY, depth, k_Wall, wallHeight);

            var body = Undo.AddComponent<Rigidbody>(root.gameObject);
            body.isKinematic = true;
            body.useGravity = false;

            // Seule la façade sert à attraper le tiroir (on peut attraper les objets à l'intérieur sans le tirer).
            var interactable = Undo.AddComponent<XRSimpleInteractable>(root.gameObject);
            interactable.colliders.Clear();
            interactable.colliders.Add(frontBox);

            var drawer = Undo.AddComponent<Drawer>(root.gameObject);
            drawer.frame = frame;
            drawer.closedPosition = frame.InverseTransformPoint(root.position);
            drawer.openDirection = axis;
            drawer.maxOpen = depth * k_OpenRatio;
            drawer.contentSpace = collisions;
            drawer.contentCenter = Vector3.up * (-height / 2f + k_Wall + wallHeight * 0.45f);
            var inner = new Vector3(width - 2f * k_Wall, wallHeight * 0.9f, depth - 2f * k_Wall);
            drawer.contentSize = Abs(axis * inner.z + side * inner.x + Vector3.up * inner.y);
            EditorUtility.SetDirty(interactable);
            return drawer;
        }

        readonly struct Box
        {
            public readonly BoxCollider collider;

            public Box(Transform parent, string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                collider = go.AddComponent<BoxCollider>();
                collider.center = center;
                collider.size = Abs(size);
            }
        }

        // ---- Cadenas et clé (formes simples, à remplacer par de vrais modèles) ----

        static KeyLock CreatePadlock(Drawer drawer, Transform frame)
        {
            var outward = frame.TransformDirection(drawer.openDirection).normalized;
            var collisions = drawer.contentSpace;
            var frontBox = collisions.Find("Façade").GetComponent<BoxCollider>();
            var frontCenter = collisions.TransformPoint(frontBox.center);
            var frontHalfDepth = k_Wall / 2f;
            var frontHeight = Mathf.Abs(Vector3.Dot(frontBox.size, Vector3.up));

            var body = new Vector3(0.045f, 0.05f, 0.02f);
            var padlock = new GameObject("Cadenas (tiroir)");
            Undo.RegisterCreatedObjectUndo(padlock, "Cadenas");
            padlock.transform.SetParent(drawer.transform, false);
            padlock.transform.localScale = InverseScale(drawer.transform);
            padlock.transform.SetPositionAndRotation(
                frontCenter + outward * (frontHalfDepth + 0.012f + body.z / 2f) + Vector3.up * (frontHeight / 2f - 0.06f),
                Quaternion.LookRotation(outward, Vector3.up));

            var gold = Metal("Cadenas laiton (provisoire)", new Color(0.78f, 0.62f, 0.25f));
            var steel = Metal("Anse acier (provisoire)", new Color(0.75f, 0.75f, 0.78f));
            var dark = WritingBoardMenu.GetOrCreateMaterial("Serrure (provisoire)", new Color(0.05f, 0.05f, 0.05f));

            Part(padlock.transform, "Corps", PrimitiveType.Cube, Vector3.zero, Quaternion.identity, body, gold);
            var shackle = new GameObject("Anse").transform;
            shackle.SetParent(padlock.transform, false);
            shackle.localPosition = new Vector3(0f, body.y / 2f, 0f);
            Part(shackle, "Branche 1", PrimitiveType.Cube, new Vector3(-0.014f, 0.014f, 0f), Quaternion.identity, new Vector3(0.006f, 0.03f, 0.006f), steel);
            Part(shackle, "Branche 2", PrimitiveType.Cube, new Vector3(0.014f, 0.014f, 0f), Quaternion.identity, new Vector3(0.006f, 0.03f, 0.006f), steel);
            Part(shackle, "Arceau", PrimitiveType.Cube, new Vector3(0f, 0.029f, 0f), Quaternion.identity, new Vector3(0.034f, 0.006f, 0.006f), steel);
            Part(padlock.transform, "Serrure", PrimitiveType.Cube, new Vector3(0f, -0.008f, body.z / 2f), Quaternion.identity, new Vector3(0.004f, 0.012f, 0.002f), dark);

            // Moraillon : languette fixée au tiroir qui passe dans l'anse (reste sur le tiroir quand le cadenas tombe).
            Part(padlock.transform, "Moraillon", PrimitiveType.Cube, new Vector3(0f, body.y / 2f + 0.015f, -0.0068f), Quaternion.identity,
                new Vector3(0.01f, 0.004f, 0.026f), steel);
            padlock.transform.Find("Moraillon").SetParent(drawer.transform, true);

            var keyhole = new GameObject("Entrée de serrure").transform;
            keyhole.SetParent(padlock.transform, false);
            keyhole.localPosition = new Vector3(0f, -0.008f, body.z / 2f);
            keyhole.localRotation = Quaternion.Euler(0f, 180f, 0f); // Z vers l'intérieur du cadenas

            var collider = padlock.AddComponent<BoxCollider>();
            collider.size = body + new Vector3(0f, 0.03f, 0f);
            collider.center = new Vector3(0f, 0.015f, 0f);

            // Physique et prise en main, activées quand le cadenas tombe.
            var rigidbody = padlock.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.mass = 0.3f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = padlock.AddComponent<XRGrabInteractable>();
            grab.farAttachMode = InteractableFarAttachMode.Near;
            grab.enabled = false;

            var keyLock = padlock.AddComponent<KeyLock>();
            keyLock.keyhole = keyhole;
            keyLock.shackle = shackle;
            keyLock.drawer = drawer;
            return keyLock;
        }

        static LockKey CreateKey(Transform frame, Bounds deskBounds, Matrix4x4 space, Drawer drawer)
        {
            var key = new GameObject("Clé du tiroir");
            Undo.RegisterCreatedObjectUndo(key, "Clé");
            // Sur le plateau, au-dessus du tiroir verrouillé.
            var above = drawer.contentSpace.position;
            var top = space.MultiplyPoint3x4(new Vector3(0f, deskBounds.max.y, 0f)).y;
            key.transform.SetPositionAndRotation(new Vector3(above.x, top + 0.005f, above.z),
                Quaternion.LookRotation(frame.right, Vector3.up) * Quaternion.Euler(0f, 0f, 90f)); // posée à plat

            var brass = Metal("Clé laiton (provisoire)", new Color(0.8f, 0.65f, 0.3f));
            // Repère : Z de l'anneau vers le bout, Y dans le plan du panneton.
            Part(key.transform, "Anneau", PrimitiveType.Cylinder, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.028f, 0.002f, 0.028f), brass);
            Part(key.transform, "Tige", PrimitiveType.Cube, new Vector3(0f, 0f, 0.033f), Quaternion.identity, new Vector3(0.004f, 0.004f, 0.04f), brass);
            Part(key.transform, "Panneton", PrimitiveType.Cube, new Vector3(0f, -0.006f, 0.046f), Quaternion.identity, new Vector3(0.003f, 0.01f, 0.012f), brass);

            var tip = new GameObject("Bout").transform;
            tip.SetParent(key.transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.053f);
            var attach = new GameObject("Prise en main").transform;
            attach.SetParent(key.transform, false);

            var collider = key.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.006f, 0.03f, 0.068f);
            collider.center = new Vector3(0f, 0f, 0.019f);

            var body = key.AddComponent<Rigidbody>();
            body.mass = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = key.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attach;
            grab.useDynamicAttach = false;
            grab.farAttachMode = InteractableFarAttachMode.Near;

            var lockKey = key.AddComponent<LockKey>();
            lockKey.tip = tip;
            return lockKey;
        }

        // ---- Utilitaires ----

        static void Part(Transform parent, string name, PrimitiveType type, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material Metal(string name, Color color)
        {
            var material = WritingBoardMenu.GetOrCreateMaterial(name, color);
            material.SetFloat("_Metallic", 0.85f);
            material.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Bounds DrawerBoundsIn(Transform root, Matrix4x4 toSpace)
        {
            var hasBounds = false;
            var result = new Bounds();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local;
                Transform space;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    local = skinned.localBounds;
                    space = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    local = filter.sharedMesh.bounds;
                    space = renderer.transform;
                }
                else
                    continue;

                var b = MeshBoundsIn(space, local, toSpace);
                if (hasBounds)
                    result.Encapsulate(b);
                else
                    result = b;
                hasBounds = true;
            }
            return result;
        }

        static Bounds MeshBoundsIn(Transform space, Bounds local, Matrix4x4 toSpace)
        {
            var result = new Bounds(toSpace.MultiplyPoint3x4(space.TransformPoint(local.center)), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(toSpace.MultiplyPoint3x4(space.TransformPoint(corner)));
            }
            return result;
        }

        static Vector3 InverseScale(Transform t)
        {
            var s = t.lossyScale;
            return new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
