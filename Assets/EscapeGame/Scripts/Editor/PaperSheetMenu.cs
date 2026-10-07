using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game > Feuille A4" : feuille attrapable, prête à recevoir un PNG de document.
    /// </summary>
    static class PaperSheetMenu
    {
        static readonly Vector2 k_A4 = new Vector2(0.21f, 0.297f); // mètres
        const float k_Thickness = 0.002f; // collider un peu plus épais que la feuille, pour une physique stable

        [MenuItem("GameObject/Escape Game/Feuille A4", false, 13)]
        static void CreatePaperSheet(MenuCommand command)
        {
            var sheet = new GameObject("Feuille A4");
            WritingBoardMenu.PlaceInFrontOfSceneView(sheet, command);
            // Posée à plat, recto vers le haut.
            sheet.transform.rotation = Quaternion.Euler(90f, sheet.transform.eulerAngles.y, 0f);

            var material = WritingBoardMenu.GetOrCreateMaterial("Feuille papier", Color.white);
            material.SetFloat("_Smoothness", 0.1f); // papier mat
            EditorUtility.SetDirty(material);

            // Recto : face visible vers -Z (sens par défaut du Quad). Verso : retourné, visible vers +Z.
            var front = CreateFace(sheet.transform, "Recto", material, -0.0002f, Quaternion.identity);
            var back = CreateFace(sheet.transform, "Verso", material, 0.0002f, Quaternion.Euler(0f, 180f, 0f));

            var collider = sheet.AddComponent<BoxCollider>();
            collider.size = new Vector3(k_A4.x, k_A4.y, k_Thickness);

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

            Undo.RegisterCreatedObjectUndo(sheet, "Créer feuille A4");
            Selection.activeGameObject = sheet;
        }

        static Renderer CreateFace(Transform parent, string name, Material material, float z, Quaternion rotation)
        {
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = name;
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(parent, false);
            face.transform.localPosition = new Vector3(0f, 0f, z);
            face.transform.localRotation = rotation;
            face.transform.localScale = new Vector3(k_A4.x, k_A4.y, 1f);
            var renderer = face.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
