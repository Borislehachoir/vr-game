using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menus "GameObject > Escape Game > Feuille A4" et "Post-it" : feuille attrapable, prête à recevoir un PNG de document.
    /// </summary>
    static class PaperSheetMenu
    {
        static readonly Vector2 k_A4 = new Vector2(0.21f, 0.297f); // mètres
        static readonly Vector2 k_PostIt = new Vector2(0.12f, 0.12f); // un peu plus grand qu'un vrai post-it (7,6 cm), pour être lisible en VR
        static readonly Color k_PostItYellow = new Color(1f, 0.93f, 0.45f);
        const float k_Thickness = 0.002f; // collider un peu plus épais que la feuille, pour une physique stable

        [MenuItem("GameObject/Escape Game/Feuille A4", false, 13)]
        static void CreatePaperSheet(MenuCommand command)
        {
            var paper = WritingBoardMenu.GetOrCreateMaterial("Feuille papier", Color.white);
            CreateSheet(command, "Feuille A4", k_A4, paper, paper);
        }

        [MenuItem("GameObject/Escape Game/Post-it", false, 14)]
        static void CreatePostIt(MenuCommand command)
        {
            // Recto blanc pour que le PNG garde ses couleurs, verso jaune de post-it.
            var front = WritingBoardMenu.GetOrCreateMaterial("Feuille papier", Color.white);
            var back = WritingBoardMenu.GetOrCreateMaterial("Post-it jaune", k_PostItYellow);
            var sheet = CreateSheet(command, "Post-it", k_PostIt, front, back);
            var paper = sheet.GetComponent<PaperSheet>();
            paper.gripFromBottom = 0.02f;
            sheet.GetComponent<Rigidbody>().mass = 0.005f;
        }

        static GameObject CreateSheet(MenuCommand command, string name, Vector2 size, Material frontMaterial, Material backMaterial)
        {
            var sheet = new GameObject(name);
            WritingBoardMenu.PlaceInFrontOfSceneView(sheet, command);
            // Posée à plat, recto vers le haut.
            sheet.transform.rotation = Quaternion.Euler(90f, sheet.transform.eulerAngles.y, 0f);

            foreach (var material in new[] { frontMaterial, backMaterial })
            {
                material.SetFloat("_Smoothness", 0.1f); // papier mat
                EditorUtility.SetDirty(material);
            }

            // Recto : face visible vers -Z (sens par défaut du Quad). Verso : retourné, visible vers +Z.
            var front = CreateFace(sheet.transform, "Recto", frontMaterial, size, -0.0002f, Quaternion.identity);
            var back = CreateFace(sheet.transform, "Verso", backMaterial, size, 0.0002f, Quaternion.Euler(0f, 180f, 0f));

            var collider = sheet.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, k_Thickness);

            var body = sheet.AddComponent<Rigidbody>();
            body.mass = 0.01f;
            body.linearDamping = 3f;  // tombe doucement, comme une feuille
            body.angularDamping = 3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            sheet.AddComponent<XRGrabInteractable>();
            var paper = sheet.AddComponent<PaperSheet>();
            paper.frontRenderer = front;
            paper.backRenderer = back;

            Undo.RegisterCreatedObjectUndo(sheet, "Créer " + name);
            Selection.activeGameObject = sheet;
            return sheet;
        }

        static Renderer CreateFace(Transform parent, string name, Material material, Vector2 size, float z, Quaternion rotation)
        {
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = name;
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(parent, false);
            face.transform.localPosition = new Vector3(0f, 0f, z);
            face.transform.localRotation = rotation;
            face.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = face.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
