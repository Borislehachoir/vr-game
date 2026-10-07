using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game > Tableau d'écriture" : crée une zone transparente où l'on peut écrire,
    /// avec un rebord, un feutre bleu et un effaceur provisoires (formes simples à remplacer par les vrais modèles).
    /// </summary>
    static class WritingBoardMenu
    {
        const string k_MaterialFolder = "Assets/EscapeGame/Materials";
        static readonly Vector2 k_BoardSize = new Vector2(1.2f, 0.8f);

        const string k_MarkerModelPath = "Assets/3D/Feutre/source/pencil.fbx";
        const string k_MarkerBodyName = "Pen";
        const string k_MarkerCapName = "Cap";
        const float k_MarkerLength = 0.14f; // longueur d'un feutre Velleda réel

        [MenuItem("GameObject/Escape Game/Tableau d'écriture (feutre + effaceur)", false, 11)]
        static void CreateWritingBoard(MenuCommand command)
        {
            var root = new GameObject("Tableau d'écriture");
            PlaceInFrontOfSceneView(root, command);

            CreateSurface(root.transform);

            var ledgeY = -k_BoardSize.y / 2f - 0.02f;
            var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = "Rebord";
            ledge.transform.SetParent(root.transform, false);
            ledge.transform.localPosition = new Vector3(0f, ledgeY, -0.05f);
            ledge.transform.localScale = new Vector3(k_BoardSize.x, 0.02f, 0.1f);
            ledge.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Rebord (provisoire)", new Color(0.35f, 0.35f, 0.38f));

            var marker = CreateMarker(root.transform);
            marker.transform.localPosition = new Vector3(-0.2f, ledgeY + 0.025f, -0.05f);
            marker.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var eraser = CreateEraser(root.transform);
            eraser.transform.localPosition = new Vector3(0.25f, ledgeY + 0.035f, -0.05f);

            Undo.RegisterCreatedObjectUndo(root, "Créer tableau d'écriture");
            Selection.activeGameObject = root;
        }

        static void CreateSurface(Transform parent)
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Zone d'écriture";
            surface.transform.SetParent(parent, false);
            surface.transform.localScale = new Vector3(k_BoardSize.x, k_BoardSize.y, 1f);

            // Collider fin en Trigger : détecté par le feutre, mais ne le repousse pas.
            Object.DestroyImmediate(surface.GetComponent<MeshCollider>());
            var collider = surface.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 0.02f);
            collider.isTrigger = true;

            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Matériau d'aperçu dans l'éditeur ; remplacé par celui de l'encre au lancement.
            renderer.sharedMaterial = GetOrCreateMaterial("Zone d'écriture (aperçu)", new Color(1f, 1f, 1f, 0.25f),
                "EscapeGame/WritableSurface", "_BackgroundColor");

            surface.AddComponent<WritableSurface>(); // Reset() assigne les shaders
        }

        static GameObject CreateMarker(Transform parent)
        {
            // L'axe Z local du feutre va du bouchon vers la pointe.
            var marker = new GameObject("Feutre bleu");
            marker.transform.SetParent(parent, false);

            var collider = marker.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.radius = 0.01f;
            collider.height = 0.13f;

            var tip = CreateChild(marker.transform, "Pointe", new Vector3(0f, 0f, 0.065f), Quaternion.identity);
            var attach = CreateChild(marker.transform, "Prise en main", new Vector3(0f, 0f, -0.02f), Quaternion.identity);

            SetupGrab(marker, attach, 0.03f);
            var tool = marker.AddComponent<WritingTool>();
            tool.mode = WritingTool.ToolMode.Ink;
            tool.tip = tip;
            tool.color = new Color(0.1f, 0.25f, 0.85f, 1f);
            tool.radius = 0.003f;

            if (!ApplyMarkerModel(tool))
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.name = "Modèle (provisoire)";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(marker.transform, false);
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                body.transform.localScale = new Vector3(0.02f, 0.065f, 0.02f); // 13 cm de long
                body.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Feutre bleu (provisoire)", new Color(0.1f, 0.25f, 0.85f));
            }

            return marker;
        }

        /// <summary>
        /// Ajoute le modèle FBX au feutre, orienté pointe vers +Z, mis à l'échelle et centré.
        /// Place la pointe au bout du modèle et ajuste le collider. Le bouchon est désactivé.
        /// </summary>
        static bool ApplyMarkerModel(WritingTool tool)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MarkerModelPath);
            if (prefab == null)
                return false;

            var marker = tool.transform;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, marker);
            model.name = "Modèle";
            var baseScale = model.transform.localScale;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var cap = model.transform.Find(k_MarkerCapName);
            if (cap != null)
                cap.gameObject.SetActive(false);

            var body = model.transform.Find(k_MarkerBodyName);
            var filter = body != null ? body.GetComponent<MeshFilter>() : model.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return false;

            // 1. Axe le plus long = axe du feutre ; la pointe est l'extrémité la plus fine.
            var bounds = LocalBounds(marker, filter, out var points);
            var axis = LongestAxis(bounds.size);
            var tipAtMax = EndWidth(points, bounds, axis, true) < EndWidth(points, bounds, axis, false);
            var tipDirection = Vector3.zero;
            tipDirection[axis] = tipAtMax ? 1f : -1f;
            model.transform.localRotation = Quaternion.FromToRotation(tipDirection, Vector3.forward);

            // 2. Mise à l'échelle à la longueur voulue, puis centrage sur l'origine du feutre.
            model.transform.localScale = baseScale * (k_MarkerLength / bounds.size[axis]);
            bounds = LocalBounds(marker, filter, out _);
            model.transform.localPosition = -bounds.center;
            var size = bounds.size;

            // 3. Pointe au bout, collider à la taille du modèle.
            var tip = tool.tip != null && tool.tip != marker ? tool.tip : CreateChild(marker, "Pointe", Vector3.zero, Quaternion.identity);
            var collider = marker.GetComponent<CapsuleCollider>();
            tip.localPosition = new Vector3(0f, 0f, size.z / 2f);
            tip.localRotation = Quaternion.identity;
            tool.tip = tip;
            if (collider != null)
            {
                collider.center = Vector3.zero;
                collider.direction = 2;
                collider.height = size.z;
                collider.radius = Mathf.Max(size.x, size.y) / 2f;
            }

            return true;
        }

        static Bounds LocalBounds(Transform space, MeshFilter filter, out Vector3[] points)
        {
            var vertices = filter.sharedMesh.vertices;
            points = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
                points[i] = space.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));

            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points)
                bounds.Encapsulate(point);
            return bounds;
        }

        static int LongestAxis(Vector3 size) => size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

        // Largeur du modèle dans les 10 % situés à une extrémité de l'axe.
        static float EndWidth(Vector3[] points, Bounds bounds, int axis, bool maxEnd)
        {
            var limit = bounds.size[axis] * 0.1f;
            var a = (axis + 1) % 3;
            var b = (axis + 2) % 3;
            var width = 0f;
            foreach (var point in points)
            {
                var fromEnd = maxEnd ? bounds.max[axis] - point[axis] : point[axis] - bounds.min[axis];
                if (fromEnd > limit)
                    continue;
                width = Mathf.Max(width, Mathf.Abs(point[a] - bounds.center[a]), Mathf.Abs(point[b] - bounds.center[b]));
            }
            return width;
        }

        static GameObject CreateEraser(Transform parent)
        {
            // La face qui efface est le dessous du pavé.
            var eraser = new GameObject("Effaceur");
            eraser.transform.SetParent(parent, false);

            var size = new Vector3(0.12f, 0.04f, 0.05f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Modèle (provisoire)";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(eraser.transform, false);
            body.transform.localScale = size;
            body.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Effaceur (provisoire)", new Color(0.85f, 0.85f, 0.8f));

            var collider = eraser.AddComponent<BoxCollider>();
            collider.size = size;

            var faceDown = Quaternion.Euler(90f, 0f, 0f); // axe Z vers le bas
            var tip = CreateChild(eraser.transform, "Pointe", new Vector3(0f, -size.y / 2f, 0f), faceDown);
            var attach = CreateChild(eraser.transform, "Prise en main", new Vector3(0f, size.y / 2f, 0f), faceDown);

            SetupGrab(eraser, attach, 0.08f);
            var tool = eraser.AddComponent<WritingTool>();
            tool.mode = WritingTool.ToolMode.Erase;
            tool.tip = tip;
            tool.radius = 0.025f;
            tool.softness = 0.5f;
            return eraser;
        }

        static void SetupGrab(GameObject go, Transform attach, float mass)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attach;
            grab.useDynamicAttach = false;
        }

        internal static Transform CreateChild(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            child.localRotation = rotation;
            return child;
        }

        internal static void PlaceInFrontOfSceneView(GameObject root, MenuCommand command)
        {
            var parent = command.context as GameObject;
            if (parent != null)
            {
                root.transform.SetParent(parent.transform, false);
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                var view = SceneView.lastActiveSceneView;
                root.transform.position = view.pivot;
                var forward = view.camera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    root.transform.rotation = Quaternion.LookRotation(forward);
            }

            GameObjectUtility.EnsureUniqueNameForSibling(root);
        }

        internal static Material GetOrCreateMaterial(string name, Color color,
            string shaderName = "Universal Render Pipeline/Lit", string colorProperty = "_BaseColor")
        {
            var path = $"{k_MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            if (!AssetDatabase.IsValidFolder(k_MaterialFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(k_MaterialFolder).Replace('\\', '/'), Path.GetFileName(k_MaterialFolder));
            material = new Material(Shader.Find(shaderName));
            material.SetColor(colorProperty, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
