using TMPro;
using UnityEditor;
using UnityEngine;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menus pour décorer un tableau d'écriture avec des éléments ineffaçables :
    /// - "Texte permanent sur le tableau" : "Code : VLA" en haut du tableau ;
    /// - "Mot à déchiffrer sur le tableau" : "Mot déchiffré : _ _ _", énigme où l'on écrit le mot au feutre.
    /// Ces éléments sont des objets séparés posés juste devant la zone d'écriture :
    /// l'effaceur ne touche que l'encre de la zone, jamais eux.
    /// </summary>
    static class PermanentBoardTextMenu
    {
        const string k_TextMenuPath = "GameObject/Escape Game/Texte permanent sur le tableau";
        const string k_WordMenuPath = "GameObject/Escape Game/Mot à déchiffrer sur le tableau";
        const string k_DefaultText = "Code : VLA";
        static readonly Color k_TextColor = new Color(0.8f, 0.1f, 0.1f, 1f); // rouge feutre
        static readonly Color k_SuccessColor = new Color(0.2f, 0.75f, 0.3f, 1f);

        const float k_Offset = 0.002f;       // distance (m) devant la zone d'écriture
        const float k_TopMargin = 0.04f;     // part de la hauteur laissée vide au-dessus du texte
        const float k_BandHeight = 0.2f;     // part de la hauteur occupée par le texte
        const float k_BandWidth = 0.9f;      // part de la largeur occupée par le texte

        // Mot à déchiffrer (en mètres).
        const string k_WordLabel = "Mot déchiffré :";
        const string k_WordSolution = "SIX";
        static readonly Vector2 k_SlotSize = new Vector2(0.16f, 0.2f);
        const float k_SlotGap = 0.08f;        // espace entre deux traits
        const float k_LineThickness = 0.008f;
        const float k_WordLineGap = 0.04f;    // espace sous "Code : VLA"
        const float k_LabelGap = 0.08f;       // espace entre le libellé et le premier trait

        [MenuItem(k_TextMenuPath, false, 12)]
        static void AddPermanentText(MenuCommand command)
        {
            var surface = FindSurface(command);
            if (surface == null)
                return;

            var size = surface.Size;
            var top = 0.5f - k_TopMargin - k_BandHeight / 2f;
            var text = CreateOnBoard(surface, "Texte permanent", new Vector2(0f, top * size.y));
            Undo.RegisterCreatedObjectUndo(text, "Ajouter texte permanent");

            var tmp = AddText(text, k_DefaultText, size.y * k_BandHeight * 10f, TextAlignmentOptions.Center);
            tmp.rectTransform.sizeDelta = new Vector2(size.x * k_BandWidth, size.y * k_BandHeight);
            // Réduit si besoin pour tenir dans la bande.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.1f;
            tmp.fontSizeMax = tmp.fontSize;

            Selection.activeGameObject = text;
        }

        [MenuItem(k_WordMenuPath, false, 13)]
        static void AddWordPuzzle(MenuCommand command)
        {
            var surface = FindSurface(command);
            if (surface == null)
                return;

            // Ligne des traits, sous la bande de "Code : VLA".
            var size = surface.Size;
            var bandBottom = size.y / 2f - size.y * (k_TopMargin + k_BandHeight);
            var lineY = bandBottom - k_WordLineGap - k_SlotSize.y;

            var root = CreateOnBoard(surface, "Mot à déchiffrer", new Vector2(0f, lineY));
            Undo.RegisterCreatedObjectUndo(root, "Ajouter mot à déchiffrer");

            // Libellé, aligné à droite et posé sur la ligne des traits.
            var labelHeight = k_SlotSize.y * 0.6f;
            var label = new GameObject("Libellé");
            label.transform.SetParent(root.transform, false);
            var tmp = AddText(label, k_WordLabel, labelHeight * 10f, TextAlignmentOptions.BottomRight);
            var labelWidth = tmp.GetPreferredValues(k_WordLabel).x;
            if (labelWidth < 0.01f)
                labelWidth = labelHeight * 0.5f * k_WordLabel.Length; // police pas encore chargée : estimation
            tmp.rectTransform.sizeDelta = new Vector2(labelWidth, labelHeight);

            // Tout l'ensemble (libellé, traits, coche) est centré sur le tableau.
            var slotsWidth = k_WordSolution.Length * k_SlotSize.x + (k_WordSolution.Length - 1) * k_SlotGap;
            var checkWidth = k_SlotSize.y * 0.8f;
            var total = labelWidth + k_LabelGap + slotsWidth + k_SlotGap + checkWidth;
            var x = -total / 2f;
            tmp.rectTransform.localPosition = new Vector3(x + labelWidth / 2f, labelHeight / 2f, 0f);
            x += labelWidth + k_LabelGap;

            var lineMaterial = GetOverlayMaterial("Tableau - trait rouge", k_TextColor);
            var slots = new Transform[k_WordSolution.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = CreateBar(root.transform, $"Trait {i + 1}", lineMaterial,
                    new Vector2(x + k_SlotSize.x / 2f, 0f), k_SlotSize.x, k_LineThickness, 0f);
                x += k_SlotSize.x + k_SlotGap;
            }

            // Coche verte (✓), cachée jusqu'à la réussite.
            var check = new GameObject("Coche de réussite");
            check.transform.SetParent(root.transform, false);
            check.transform.localPosition = new Vector3(x + checkWidth / 2f, k_SlotSize.y * 0.45f, 0f);
            var checkMaterial = GetOverlayMaterial("Tableau - coche verte", k_SuccessColor);
            var s = checkWidth / 0.16f;
            CreateStroke(check.transform, "Petit trait", checkMaterial, new Vector2(-0.07f, 0f) * s, new Vector2(-0.025f, -0.06f) * s, 0.02f * s);
            CreateStroke(check.transform, "Grand trait", checkMaterial, new Vector2(-0.032f, -0.06f) * s, new Vector2(0.08f, 0.09f) * s, 0.02f * s);
            check.SetActive(false);

            var puzzle = root.AddComponent<BoardWordPuzzle>();
            puzzle.puzzleId = "Mot déchiffré";
            puzzle.solution = k_WordSolution;
            puzzle.surface = surface;
            puzzle.slots = slots;
            puzzle.slotSize = k_SlotSize;
            puzzle.successMark = check;

            Selection.activeGameObject = root;
        }

        [MenuItem(k_TextMenuPath, true)]
        [MenuItem(k_WordMenuPath, true)]
        static bool ValidateBoardSelected() => Selection.activeGameObject != null;

        // Objet rangé à côté de la zone (pas dedans : elle est étirée et déformerait son contenu),
        // à l'échelle 1 (une unité = un mètre), posé sur la face où l'on écrit (la face visible du Quad regarde vers -Z).
        static GameObject CreateOnBoard(WritableSurface surface, string name, Vector2 boardPoint)
        {
            var board = surface.transform;
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(board.parent, false);
            var parentScale = board.parent != null ? board.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

            var size = surface.Size;
            var local = new Vector3(boardPoint.x / size.x, boardPoint.y / size.y, 0f);
            t.SetPositionAndRotation(board.TransformPoint(local) - board.forward * k_Offset, board.rotation);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            return go;
        }

        static TextMeshPro AddText(GameObject go, string text, float fontSize, TextAlignmentOptions alignment)
        {
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.color = k_TextColor;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.fontSize = fontSize; // ≈ 10 unités de police par mètre de hauteur de ligne

            // Toujours dessiné par-dessus le fond semi-transparent de la zone.
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 1;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return tmp;
        }

        static Transform CreateBar(Transform parent, string name, Material material, Vector2 center, float length, float thickness, float angle)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bar.name = name;
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = center;
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            bar.transform.localScale = new Vector3(length, thickness, 1f);

            var renderer = bar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return bar.transform;
        }

        static void CreateStroke(Transform parent, string name, Material material, Vector2 from, Vector2 to, float thickness)
        {
            var delta = to - from;
            CreateBar(parent, name, material, (from + to) / 2f, delta.magnitude, thickness,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // Couleur unie, dessinée après le fond semi-transparent de la zone d'écriture.
        static Material GetOverlayMaterial(string name, Color color)
        {
            var material = WritingBoardMenu.GetOrCreateMaterial(name, color, "Universal Render Pipeline/Unlit");
            if (material.renderQueue != 3001)
            {
                material.renderQueue = 3001;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        static WritableSurface FindSurface(MenuCommand command)
        {
            var go = command.context as GameObject ?? Selection.activeGameObject;
            WritableSurface surface = null;
            if (go != null)
            {
                surface = go.GetComponentInChildren<WritableSurface>();
                if (surface == null)
                    surface = go.GetComponentInParent<WritableSurface>();
            }

            if (surface == null)
                EditorUtility.DisplayDialog("Tableau d'écriture",
                    "Sélectionne le tableau (ou sa « Zone d'écriture ») dans la Hierarchy, puis relance le menu.", "OK");
            return surface;
        }
    }
}
