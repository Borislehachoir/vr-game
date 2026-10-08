using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "Escape Game > Menu > Ajouter le menu à la scène Menu" :
    /// fabrique le prefab "Menu principal" (écran de télé : neige, logo, bouton Jouer) et le pose dans
    /// la scène Assets/Scenes/Menu.unity. Aucune autre scène n'est ouverte ni modifiée.
    /// Au lancement, le prefab trouve lui-même la télé, le canapé et le joueur (voir MainMenu).
    /// Relancer le menu reconstruit le prefab et remplace celui de la scène Menu.
    /// </summary>
    static class MainMenuMenu
    {
        const string k_MenuScenePath = "Assets/Scenes/Menu.unity";
        const string k_PrefabPath = "Assets/EscapeGame/Menu/Menu principal.prefab";
        const string k_RootName = "Menu principal";

        const string k_FontPath = "Assets/Fonts/Emblema_One/EmblemaOne-Regular.ttf";
        const string k_FontAssetPath = "Assets/Fonts/Emblema_One/EmblemaOne-Regular SDF.asset";
        const string k_LogoPath = "Assets/EscapeGame/Menu/Logo Alone At Night.png";

        // Écran 16:9 en unités UI ; au lancement, MainMenu l'ajuste à la taille réelle de la dalle.
        static readonly Vector2 k_ScreenSize = new Vector2(1600f, 900f);

        // Couleurs de la page itch.io.
        static readonly Color k_Cream = new Color32(236, 226, 208, 255);
        static readonly Color k_Amber = new Color32(214, 160, 92, 255);
        static readonly Color k_Muted = new Color32(190, 170, 140, 255);
        static readonly Color k_Dim = new Color32(138, 133, 124, 255);

        [MenuItem("Escape Game/Menu/Ajouter le menu à la scène Menu")]
        static void AddMenuToMenuScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_MenuScenePath) == null)
            {
                EditorUtility.DisplayDialog("Menu principal",
                    $"Scène introuvable : {k_MenuScenePath}\n\nCréer d'abord la scène Menu (la pièce avec le canapé et la télé).", "OK");
                return;
            }

            // Les modifications en cours sur la scène ouverte restent à l'utilisateur : Unity lui demande quoi en faire.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var prefab = BuildPrefab();
            if (prefab == null)
                return;

            var scene = EditorSceneManager.OpenScene(k_MenuScenePath, OpenSceneMode.Single);
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == k_RootName))
                Object.DestroyImmediate(old);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = k_RootName;

            // Ce que le menu trouvera au lancement, pour prévenir tout de suite s'il manque quelque chose.
            var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            bool Has(params string[] names) =>
                transforms.Any(t => names.Any(n => string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)));
            var hasTv = transforms.Any(t => t.GetComponent<TvScreen>() != null) || Has("TV", "Télé", "Television");
            var hasSofa = Has("sofa", "Canapé", "Canape", "Couch");
            var hasPlayer = transforms.Any(t => t.GetComponent<Unity.XR.CoreUtils.XROrigin>() != null);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            PutMenuSceneFirstInBuild();
            Selection.activeGameObject = instance;

            EditorUtility.DisplayDialog("Menu principal",
                "Menu ajouté à la scène Menu, scène enregistrée.\n\n" +
                "Au lancement :\n" +
                "1. le joueur apparaît assis sur le canapé, face à la télé (il ne peut que tourner la tête)\n" +
                "2. la télé grésille, puis affiche le logo et le bouton Jouer\n" +
                "3. « Jouer » : la télé s'éteint, le joueur se lève devant le canapé et peut se déplacer\n\n" +
                $"• Télé : {(hasTv ? "trouvée" : "⚠ INTROUVABLE (objet « TV »)")}\n" +
                $"• Canapé : {(hasSofa ? "trouvé" : "⚠ INTROUVABLE (objet « sofa »)")}\n" +
                $"• Joueur (XR Origin) : {(hasPlayer ? "trouvé" : "⚠ INTROUVABLE")}\n" +
                "• Scène Menu placée en premier dans la liste des scènes du build\n\n" +
                "Aucune autre scène n'a été modifiée.", "OK");
        }

        // Scène Menu en premier : c'est elle qui se lance au démarrage du jeu.
        static void PutMenuSceneFirstInBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != k_MenuScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(k_MenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------- Prefab ----------

        static GameObject BuildPrefab()
        {
            var font = GetOrCreateFontAsset();
            var logo = GetSprite(k_LogoPath);

            var root = new GameObject(k_RootName);
            try
            {
                var menu = root.AddComponent<MainMenu>();

                var canvasGo = new GameObject("Écran télé", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                    typeof(TrackedDeviceGraphicRaycaster));
                canvasGo.layer = LayerMask.NameToLayer("UI");
                canvasGo.transform.SetParent(root.transform, false);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // texte net en VR
                var screen = canvasGo.GetComponent<RectTransform>();
                screen.sizeDelta = k_ScreenSize;
                screen.localScale = Vector3.one * 0.001f; // taille provisoire, recalculée au lancement
                menu.screen = screen;

                // Dalle noire : la télé "éteinte" avant le grésillement.
                var black = NewImage(screen, "Noir", Color.black);
                black.raycastTarget = false;
                Stretch(black.rectTransform);

                // Menu : fond, logo et bouton Jouer, qui apparaissent en fondu.
                var menuPage = new GameObject("Menu", typeof(RectTransform), typeof(CanvasGroup));
                menuPage.layer = canvasGo.layer;
                menuPage.transform.SetParent(screen, false);
                Stretch((RectTransform)menuPage.transform);
                menu.menuGroup = menuPage.GetComponent<CanvasGroup>();
                menu.playButton = BuildMenuPage(menuPage.transform, font, logo);

                // Neige par-dessus tout (la texture est générée au lancement).
                var noise = new GameObject("Neige", typeof(RectTransform), typeof(RawImage));
                noise.layer = canvasGo.layer;
                noise.transform.SetParent(screen, false);
                Stretch((RectTransform)noise.transform);
                menu.staticImage = noise.GetComponent<RawImage>();
                menu.staticImage.raycastTarget = false;
                menu.staticImage.enabled = false;

                return PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Button BuildMenuPage(Transform page, TMP_FontAsset titleFont, Sprite logo)
        {
            var bg = NewImage(page, "Fond", new Color32(10, 11, 13, 255));
            Stretch(bg.rectTransform);

            // Logo à gauche (la porte remplace le « o », la clé le « t »), légèrement teinté crème.
            const float logoHeight = 800f;
            if (logo != null)
            {
                var image = NewImage(page, "Logo", k_Cream);
                image.sprite = logo;
                image.preserveAspect = true;
                var width = logoHeight * logo.rect.width / logo.rect.height;
                Place(image.rectTransform, new Vector2(-360f, 0f), new Vector2(width, logoHeight));
            }
            else
            {
                var amber = ColorUtility.ToHtmlStringRGB(k_Amber);
                var title = NewText(page, "Logo", $"Alone\n<color=#{amber}>At</color>\nNight", 150, k_Cream);
                if (titleFont != null)
                    title.font = titleFont;
                title.alignment = TextAlignmentOptions.Center;
                Place(title.rectTransform, new Vector2(-360f, 0f), new Vector2(720f, logoHeight));
            }

            // À droite : sous-titre et bouton Jouer.
            const float right = 400f;
            var subtitle = NewText(page, "Sous-titre", "ESCAPE GAME VR", 34, k_Muted);
            subtitle.characterSpacing = 25f;
            subtitle.alignment = TextAlignmentOptions.Center;
            Place(subtitle.rectTransform, new Vector2(right, 110f), new Vector2(600f, 50f));
            var lineLeft = NewImage(page, "Filet gauche", k_Amber);
            Place(lineLeft.rectTransform, new Vector2(right - 270f, 110f), new Vector2(60f, 3f));
            var lineRight = NewImage(page, "Filet droit", k_Amber);
            Place(lineRight.rectTransform, new Vector2(right + 270f, 110f), new Vector2(60f, 3f));

            // Bouton Jouer : cadre ambré, qui se remplit quand le rayon le survole.
            var frame = NewImage(page, "Jouer", k_Amber);
            frame.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            frame.type = Image.Type.Sliced;
            Place(frame.rectTransform, new Vector2(right, -20f), new Vector2(420f, 110f));
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.15f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.55f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.15f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.85f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Cadre fixe (4 traits) : reste bien visible, alors que le fond du bouton ne s'allume qu'au survol.
            var w = frame.rectTransform.sizeDelta.x;
            var h = frame.rectTransform.sizeDelta.y;
            foreach (var (pos, size) in new[]
                     {
                         (new Vector2(0f, h / 2f), new Vector2(w, 3f)), (new Vector2(0f, -h / 2f), new Vector2(w, 3f)),
                         (new Vector2(-w / 2f, 0f), new Vector2(3f, h)), (new Vector2(w / 2f, 0f), new Vector2(3f, h)),
                     })
                Place(NewImage(frame.transform, "Cadre", k_Amber).rectTransform, pos, size);

            var label = NewText(frame.transform, "Label", "JOUER", 56, k_Cream);
            label.characterSpacing = 15f;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);

            var hint = NewText(page, "Aide", "Visez « Jouer » avec la manette\net appuyez sur la gâchette", 26, k_Dim);
            hint.alignment = TextAlignmentOptions.Center;
            Place(hint.rectTransform, new Vector2(right, -150f), new Vector2(600f, 80f));

            // Seuls le fond (bloque les rayons derrière l'écran) et le bouton reçoivent les rayons.
            foreach (var graphic in page.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = graphic == bg || graphic == frame;
            return button;
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
            return text;
        }

        // Position mesurée depuis le centre de l'écran.
        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect) => PuzzleScreenMenu.Stretch(rect);

        // ---------- Ressources ----------

        // Police du logo (comme la page itch.io). Font asset TextMeshPro créé une seule fois à côté du .ttf.
        static TMP_FontAsset GetOrCreateFontAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontAssetPath);
            if (asset != null)
                return asset;

            var font = AssetDatabase.LoadAssetAtPath<Font>(k_FontPath);
            if (font == null)
            {
                Debug.LogWarning($"Police introuvable : {k_FontPath}. Le logo utilisera la police par défaut.");
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

        static Sprite GetSprite(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true; // vu de loin dans le casque : évite le scintillement
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
