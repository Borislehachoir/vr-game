using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "Escape Game > Menu > Créer le menu sur la télé" : construit le menu principal sur l'écran de la télé
    /// et assoit le joueur sur le canapé, face à elle. Relancer le menu remplace le menu existant.
    /// </summary>
    static class MainMenuMenu
    {
        const string k_RootName = "Menu principal";
        static readonly string[] k_TvNames = { "TV", "Télé", "Tele", "Television", "Télévision" };
        static readonly string[] k_SofaNames = { "sofa", "Canapé", "Canape", "Couch" };

        const string k_FontPath = "Assets/Fonts/Emblema_One/EmblemaOne-Regular.ttf";
        const string k_FontAssetPath = "Assets/Fonts/Emblema_One/EmblemaOne-Regular SDF.asset";
        const string k_BackgroundPath = "Assets/EscapeGame/Menu/Fond menu.jpg";

        // Écran 16:9 en unités UI ; l'échelle du Canvas l'ajuste à la taille réelle de la télé.
        static readonly Vector2 k_ScreenSize = new Vector2(1600f, 900f);
        const float k_ScreenGap = 0.006f;     // distance (m) devant la dalle, évite que l'écran clignote dans la télé
        const float k_Bezel = 0.05f;          // part de la largeur / hauteur réservée au cadre de la télé

        // Joueur.
        const float k_SeatEyeHeight = 1.15f;  // hauteur des yeux d'une personne assise (m)
        const float k_SeatForward = 0.1f;     // décalage de l'assise vers l'avant du canapé (m)
        const float k_StandDistance = 0.5f;   // distance devant le canapé où le joueur se lève (m)

        // Couleurs de la page itch.io.
        static readonly Color k_Cream = new Color32(236, 226, 208, 255);
        static readonly Color k_Amber = new Color32(214, 160, 92, 255);
        static readonly Color k_Muted = new Color32(190, 170, 140, 255);
        static readonly Color k_Dim = new Color32(138, 133, 124, 255);
        const float k_Margin = 110f;

        [MenuItem("Escape Game/Menu/Créer le menu sur la télé (scène ouverte)")]
        static void CreateMenu()
        {
            var scene = SceneManager.GetActiveScene();
            var tv = FindByName(scene, k_TvNames);
            var sofa = FindByName(scene, k_SofaNames);
            var origin = Object.FindFirstObjectByType<XROrigin>();

            var missing = (tv == null ? "• la télé (objet nommé « TV »)\n" : "") +
                          (sofa == null ? "• le canapé (objet nommé « sofa »)\n" : "") +
                          (origin == null ? "• le joueur (XR Origin)\n" : "");
            if (missing.Length > 0)
            {
                EditorUtility.DisplayDialog("Menu principal", "Introuvable dans la scène :\n" + missing, "OK");
                return;
            }

            if (!TryGetScreen(tv, sofa, out var screenCenter, out var screenNormal, out var screenSize))
            {
                EditorUtility.DisplayDialog("Menu principal", "La télé n'a pas de modèle 3D (MeshFilter).", "OK");
                return;
            }

            var font = GetOrCreateFontAsset();
            var background = GetBackgroundSprite();
            PuzzleScreenMenu.EnsureXREventSystem();

            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == k_RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            var root = new GameObject(k_RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Créer le menu principal");
            var menu = root.AddComponent<MainMenu>();

            // --- Écran ---
            var canvas = CreateCanvas(root.transform, screenCenter + screenNormal * k_ScreenGap, screenNormal, screenSize);
            menu.screen = canvas.GetComponent<CanvasGroup>();
            menu.lookTarget = canvas.transform;
            BuildScreen(canvas.transform, menu, font, background);

            // --- Joueur assis / debout ---
            var sofaBounds = GetBounds(sofa);
            var floor = sofaBounds.min.y;
            var toTv = screenCenter - sofaBounds.center;
            toTv.y = 0f;
            toTv.Normalize();
            var halfDepth = Mathf.Abs(toTv.x) * sofaBounds.extents.x + Mathf.Abs(toTv.z) * sofaBounds.extents.z;
            var seat = new Vector3(sofaBounds.center.x, floor, sofaBounds.center.z) + toTv * k_SeatForward;

            menu.seatEyes = CreatePoint(root.transform, "Assise (yeux)", seat + Vector3.up * k_SeatEyeHeight, toTv);
            menu.standPoint = CreatePoint(root.transform, "Point de départ (debout)", seat + toTv * (halfDepth + k_StandDistance), toTv);
            menu.origin = origin;

            // Le joueur démarre sur le canapé, face à la télé (aussi dans l'éditeur).
            Undo.RecordObject(origin.transform, "Placer le joueur sur le canapé");
            origin.transform.SetPositionAndRotation(seat, Quaternion.LookRotation(toTv, Vector3.up));

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();

            EditorUtility.DisplayDialog("Menu principal",
                "Menu créé sur l'écran de la télé :\n" +
                "• Jouer / Commandes / Quitter, au style de la page itch.io\n" +
                "• Le joueur démarre assis sur le canapé, face à la télé\n" +
                "• Déplacements et rotations bloqués jusqu'à « Jouer »\n\n" +
                "Si l'écran déborde du cadre ou ne colle pas à la dalle : sélectionner « Menu principal > Écran télé » " +
                "et ajuster sa position / son échelle. Même chose pour « Assise (yeux) » et « Point de départ (debout) ».\n\n" +
                "Penser à enregistrer la scène (Ctrl+S).", "OK");
        }

        // ---------- Recherche de la télé et de l'écran ----------

        static GameObject FindByName(Scene scene, string[] names)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (names.Any(n => string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)))
                    return t.gameObject;
            return null;
        }

        static Bounds GetBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // Face de la télé tournée vers le canapé : centre, normale (horizontale) et taille utile de la dalle (m).
        static bool TryGetScreen(GameObject tv, GameObject sofa, out Vector3 center, out Vector3 normal, out Vector2 size)
        {
            center = normal = Vector3.zero;
            size = Vector2.zero;

            var filter = tv.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null && f.GetComponent<Renderer>() != null)
                .OrderByDescending(f => f.GetComponent<Renderer>().bounds.size.sqrMagnitude)
                .FirstOrDefault();
            if (filter == null)
                return false;

            var t = filter.transform;
            var local = filter.sharedMesh.bounds;
            var toSofa = GetBounds(sofa).center - t.TransformPoint(local.center);
            toSofa.y = 0f;

            // Axes locaux du modèle exprimés dans le monde, avec leur longueur réelle (échelle comprise).
            var axes = new[] { t.right, t.up, t.forward };
            var scale = t.lossyScale;
            var lengths = new[] { local.size.x * Mathf.Abs(scale.x), local.size.y * Mathf.Abs(scale.y), local.size.z * Mathf.Abs(scale.z) };
            var localAxes = new[] { Vector3.right, Vector3.up, Vector3.forward };

            // Face avant : l'axe le plus aligné avec la direction du canapé.
            var front = Enumerable.Range(0, 3).OrderByDescending(i => Mathf.Abs(Vector3.Dot(axes[i], toSofa.normalized))).First();
            var sign = Mathf.Sign(Vector3.Dot(axes[front], toSofa));
            // Hauteur : l'axe le plus vertical parmi les deux autres ; largeur : le dernier.
            var others = Enumerable.Range(0, 3).Where(i => i != front).ToArray();
            var vertical = others.OrderByDescending(i => Mathf.Abs(Vector3.Dot(axes[i], Vector3.up))).First();
            var horizontal = others.First(i => i != vertical);

            normal = axes[front] * sign;
            normal.y = 0f;
            normal.Normalize();
            center = t.TransformPoint(local.center + localAxes[front] * sign * local.extents[front]);

            var width = lengths[horizontal];
            var height = lengths[vertical];
            // Télé posée sur un pied : la boîte englobante est trop haute. On garde une dalle 16:9 en haut.
            var screenHeight = width * 9f / 16f;
            if (height > screenHeight * 1.15f)
            {
                var top = filter.GetComponent<Renderer>().bounds.max.y;
                center.y = top - screenHeight / 2f - width * k_Bezel / 2f;
                height = screenHeight;
            }

            size = new Vector2(width * (1f - 2f * k_Bezel), height * (1f - 2f * k_Bezel));
            return true;
        }

        static Transform CreatePoint(Transform parent, string name, Vector3 position, Vector3 forward)
        {
            var point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            return point;
        }

        // ---------- Écran ----------

        static GameObject CreateCanvas(Transform parent, Vector3 position, Vector3 normal, Vector2 sizeMeters)
        {
            var go = new GameObject("Écran télé", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup), typeof(TrackedDeviceGraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // texte net en VR

            // Mise à l'échelle : la zone 1600 x 900 tient dans la dalle, le reste est rempli par le fond.
            var scale = Mathf.Min(sizeMeters.x / k_ScreenSize.x, sizeMeters.y / k_ScreenSize.y);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = sizeMeters / scale;
            rect.localScale = Vector3.one * scale;
            // L'UI se lit depuis l'arrière de son axe Z : on la tourne pour qu'elle regarde le canapé.
            rect.SetPositionAndRotation(position, Quaternion.LookRotation(-normal, Vector3.up));
            return go;
        }

        static void BuildScreen(Transform screen, MainMenu menu, TMP_FontAsset titleFont, Sprite background)
        {
            // Fond : la porte entrouverte, assombrie à gauche pour lire le texte.
            var bg = NewImage(screen, "Fond", Color.white);
            Stretch(bg.rectTransform);
            bg.sprite = background;
            bg.color = background != null ? Color.white : new Color32(10, 11, 13, 255);
            bg.raycastTarget = true; // bloque les rayons derrière l'écran

            var shade = NewImage(screen, "Ombre gauche", new Color(0f, 0f, 0f, 0.55f));
            Place(shade.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), Vector2.zero, Vector2.zero);
            shade.raycastTarget = false;

            // Titre.
            var title = NewText(screen, "Titre", $"Alone <color=#{ColorUtility.ToHtmlStringRGB(k_Amber)}>At</color> Night", 120, k_Cream);
            if (titleFont != null)
                title.font = titleFont;
            title.alignment = TextAlignmentOptions.BottomLeft;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            PlaceTopLeft(title.rectTransform, new Vector2(k_Margin, -110f), new Vector2(1300f, 180f));

            var line = NewImage(screen, "Filet", k_Amber);
            line.raycastTarget = false;
            PlaceTopLeft(line.rectTransform, new Vector2(k_Margin + 4f, -333f), new Vector2(70f, 3f));
            var subtitle = NewText(screen, "Sous-titre", "ESCAPE GAME VR", 30, k_Muted);
            subtitle.characterSpacing = 25f;
            subtitle.alignment = TextAlignmentOptions.Left;
            PlaceTopLeft(subtitle.rectTransform, new Vector2(k_Margin + 100f, -312f), new Vector2(700f, 44f));

            // Page principale.
            var main = NewPage(screen, "Page principale");
            AddButton(main, "Jouer", -420f, menu.Play);
            AddButton(main, "Commandes", -510f, menu.ShowControls);
            AddButton(main, "Quitter", -600f, menu.Quit);
            menu.mainPage = main.gameObject;

            // Page des commandes.
            var controls = NewPage(screen, "Page commandes");
            var help = NewText(controls, "Commandes",
                "<color=#" + ColorUtility.ToHtmlStringRGB(k_Amber) + ">Joystick gauche</color>   se déplacer\n" +
                "<color=#" + ColorUtility.ToHtmlStringRGB(k_Amber) + ">Joystick droit</color>   tourner\n" +
                "<color=#" + ColorUtility.ToHtmlStringRGB(k_Amber) + ">Grip</color>   attraper un objet\n" +
                "<color=#" + ColorUtility.ToHtmlStringRGB(k_Amber) + ">Gâchette</color>   interagir (boutons, digicode)", 34, k_Cream);
            help.alignment = TextAlignmentOptions.TopLeft;
            help.lineSpacing = 30f;
            PlaceTopLeft(help.rectTransform, new Vector2(k_Margin, -410f), new Vector2(900f, 260f));
            AddButton(controls, "Retour", -700f, menu.ShowMain);
            controls.gameObject.SetActive(false);
            menu.controlsPage = controls.gameObject;

            // Aide en bas de l'écran.
            var hint = NewText(screen, "Aide", "Visez avec la manette et appuyez sur la gâchette", 24, k_Dim);
            hint.alignment = TextAlignmentOptions.BottomLeft;
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(1000f, 40f), new Vector2(k_Margin, 50f));
        }

        static RectTransform NewPage(Transform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            page.gameObject.layer = parent.gameObject.layer;
            page.SetParent(parent, false);
            Stretch(page);
            return page;
        }

        // Bouton texte à gauche : fond ambré invisible, qui s'allume quand le rayon le survole.
        static void AddButton(Transform parent, string label, float top, UnityAction action)
        {
            var image = NewImage(parent, label, k_Amber);
            PlaceTopLeft(image.rectTransform, new Vector2(k_Margin - 24f, top), new Vector2(440f, 74f));

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.3f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            UnityEventTools.AddPersistentListener(button.onClick, action);

            var accent = NewImage(image.transform, "Trait", k_Amber);
            accent.raycastTarget = false;
            Place(accent.rectTransform, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), new Vector2(0f, 0.5f), new Vector2(4f, 0f));

            var text = NewText(image.transform, "Label", label, 42, k_Cream);
            text.alignment = TextAlignmentOptions.Left;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24f, 0f);
        }

        static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static TextMeshProUGUI NewText(Transform parent, string name, string content, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.richText = true;
            text.raycastTarget = false;
            return text;
        }

        static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size) =>
            Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), size, position);

        static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size,
            Vector2 position = default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        static void Stretch(RectTransform rect) => PuzzleScreenMenu.Stretch(rect);

        // ---------- Ressources ----------

        // Police du titre (comme la page itch.io). Font asset TextMeshPro créé une seule fois à côté du .ttf.
        static TMP_FontAsset GetOrCreateFontAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontAssetPath);
            if (asset != null)
                return asset;

            var font = AssetDatabase.LoadAssetAtPath<Font>(k_FontPath);
            if (font == null)
            {
                Debug.LogWarning($"Police introuvable : {k_FontPath}. Le titre utilisera la police par défaut.");
                return null;
            }

            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            if (asset == null)
                return null;
            asset.name = "EmblemaOne-Regular SDF";
            AssetDatabase.CreateAsset(asset, k_FontAssetPath);
            asset.atlasTextures[0].name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static Sprite GetBackgroundSprite()
        {
            if (AssetImporter.GetAtPath(k_BackgroundPath) is TextureImporter importer &&
                importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.mipmapEnabled = true; // vu de loin dans le casque : évite le scintillement
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(k_BackgroundPath);
        }
    }
}
