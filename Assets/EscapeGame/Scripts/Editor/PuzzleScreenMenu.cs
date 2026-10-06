using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game" : crée des écrans d'énigme prêts à l'emploi dans la scène.
    /// </summary>
    static class PuzzleScreenMenu
    {
        const float k_MetersPerUnit = 0.001f; // 1000 unités UI = 1 m dans le monde

        static readonly Color k_PanelColor = new Color(0f, 0f, 0f, 0.75f);
        static readonly Color k_SubmitColor = new Color(0.25f, 0.62f, 0.38f, 1f);
        static readonly Color k_TextColor = new Color(0.92f, 0.94f, 0.97f, 1f);

        [MenuItem("GameObject/Escape Game/Écran d'énigme - Cadenas à code", false, 10)]
        static void CreateDialCodeScreen(MenuCommand command)
        {
            const int digitCount = 3;
            const float columnSpacing = 130f;

            EnsureXREventSystem();

            var root = CreateScreenRoot("Cadenas", command, new Vector2(480, 470));
            var puzzle = root.AddComponent<DialCodePuzzle>();
            puzzle.puzzleId = root.name;
            puzzle.digitTexts = new TMP_Text[digitCount];

            CreatePanelBackground(root.transform);

            for (var i = 0; i < digitCount; i++)
            {
                var x = (i - (digitCount - 1) / 2f) * columnSpacing;
                var column = new GameObject("Chiffre " + (i + 1), typeof(RectTransform));
                SetupRect(column, root.transform, new Vector2(x, -25), new Vector2(110, 270));

                CreateArrow(column.transform, "Fleche haut", DialCodeButton.ButtonAction.Up, i, new Vector2(0, 0));
                puzzle.digitTexts[i] = CreateText(column.transform, "Valeur", "0", 80, FontStyles.Bold, new Vector2(0, -75), new Vector2(110, 100));
                var line = CreateImage(column.transform, "Trait", k_TextColor, new Vector2(0, -180), new Vector2(90, 6));
                line.sprite = null;
                CreateArrow(column.transform, "Fleche bas", DialCodeButton.ButtonAction.Down, i, new Vector2(0, -195));
            }

            var submit = CreateImage(root.transform, "Valider", k_SubmitColor, new Vector2(0, -315), new Vector2(240, 70));
            AddButton(submit.gameObject, submit);
            submit.gameObject.AddComponent<DialCodeButton>().action = DialCodeButton.ButtonAction.Submit;
            Stretch(CreateText(submit.transform, "Label", "Valider", 34, FontStyles.Bold, Vector2.zero, Vector2.zero).rectTransform);

            puzzle.feedback = CreateText(root.transform, "Message", "", 26, FontStyles.Bold, new Vector2(0, -400), new Vector2(440, 45));

            Undo.RegisterCreatedObjectUndo(root, "Créer cadenas à code");
            Selection.activeGameObject = root;
        }

        static void CreateArrow(Transform parent, string name, DialCodeButton.ButtonAction action, int digitIndex, Vector2 topPosition)
        {
            // Zone cliquable invisible (plus grande que la flèche, plus facile à viser en VR).
            var hitArea = CreateImage(parent, name, Color.clear, topPosition, new Vector2(110, 70));
            hitArea.sprite = null;

            var arrow = CreateImage(hitArea.transform, "Icone", k_TextColor, Vector2.zero, Vector2.zero);
            arrow.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            arrow.type = Image.Type.Simple;
            arrow.preserveAspect = true;
            arrow.raycastTarget = false;
            var rect = arrow.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(56, 56);
            if (action == DialCodeButton.ButtonAction.Up)
                rect.localRotation = Quaternion.Euler(0, 0, 180); // le sprite pointe vers le bas

            AddButton(hitArea.gameObject, arrow);
            var button = hitArea.gameObject.AddComponent<DialCodeButton>();
            button.action = action;
            button.digitIndex = digitIndex;
        }

        static void AddButton(GameObject go, Graphic target)
        {
            var button = go.AddComponent<Button>();
            button.targetGraphic = target;
            var colors = button.colors;
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        static GameObject CreateScreenRoot(string name, MenuCommand command, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup), typeof(TrackedDeviceGraphicRaycaster), typeof(PuzzleScreen));
            root.layer = LayerMask.NameToLayer("UI");

            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // texte net en VR

            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * k_MetersPerUnit;

            var parent = command.context as GameObject;
            if (parent != null)
            {
                root.transform.SetParent(parent.transform, false);
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                // Posé là où regarde la vue Scene, face à la caméra, à hauteur des yeux du pivot.
                var view = SceneView.lastActiveSceneView;
                root.transform.position = view.pivot;
                var forward = view.camera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    root.transform.rotation = Quaternion.LookRotation(forward);
            }

            GameObjectUtility.EnsureUniqueNameForSibling(root);
            return root;
        }

        static void CreatePanelBackground(Transform parent)
        {
            var bg = CreateImage(parent, "Fond", k_PanelColor, Vector2.zero, Vector2.zero);
            Stretch(bg.rectTransform);
            bg.raycastTarget = true; // bloque le rayon : évite de téléporter "à travers" l'écran
        }

        static Image CreateImage(Transform parent, string name, Color color, Vector2 topPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            SetupRect(go, parent, topPosition, size);
            var image = go.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize,
            FontStyles style, Vector2 topPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            SetupRect(go, parent, topPosition, size);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = k_TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        // Ancre en haut au centre : topPosition.y est négatif, mesuré depuis le haut du parent.
        static void SetupRect(GameObject go, Transform parent, Vector2 topPosition, Vector2 size)
        {
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = topPosition;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// Les rayons / doigts XR ne cliquent sur l'UI que si l'EventSystem utilise XRUIInputModule.
        /// </summary>
        static void EnsureXREventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                Undo.RegisterCreatedObjectUndo(go, "Créer EventSystem XR");
                return;
            }

            if (eventSystem.GetComponent<XRUIInputModule>() != null)
                return;

            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                Undo.DestroyObjectImmediate(module);
            Undo.AddComponent<XRUIInputModule>(eventSystem.gameObject);
            Debug.Log("EventSystem : module d'entrée remplacé par XRUIInputModule pour l'UI en VR.", eventSystem);
        }
    }
}
