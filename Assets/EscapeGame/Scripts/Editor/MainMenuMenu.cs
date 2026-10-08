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
            // Texture affichée telle quelle (RawImage) : pas besoin de réglage d'import "Sprite".
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(k_LogoPath);
            if (logo == null)
                Debug.LogWarning($"Menu principal : logo introuvable : {k_LogoPath}");

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
                BuildMenuPage(menuPage.transform, menu, font, logo);

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

        static void BuildMenuPage(Transform page, MainMenu menu, TMP_FontAsset titleFont, Texture2D logo)
        {
            var bg = NewImage(page, "Fond", new Color32(10, 11, 13, 255));
            Stretch(bg.rectTransform);
            bg.raycastTarget = true; // bloque les rayons derrière l'écran

            // Logo à gauche (la porte remplace le « o », la clé le « t »), légèrement teinté crème.
            const float logoHeight = 800f;
            if (logo != null)
            {
                var go = new GameObject("Logo", typeof(RectTransform), typeof(RawImage));
                go.layer = page.gameObject.layer;
                go.transform.SetParent(page, false);
                var image = go.GetComponent<RawImage>();
                image.texture = logo;
                image.color = k_Cream;
                image.raycastTarget = false;
                Place(image.rectTransform, new Vector2(-360f, 0f), new Vector2(logoHeight * logo.width / logo.height, logoHeight));
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

            // À droite : page principale (Jouer / Commandes / Quitter) ou page des commandes.
            const float right = 400f;
            var main = NewPage(page, "Page principale");
            var subtitle = NewText(main, "Sous-titre", "ESCAPE GAME VR", 34, k_Muted);
            subtitle.characterSpacing = 25f;
            subtitle.alignment = TextAlignmentOptions.Center;
            Place(subtitle.rectTransform, new Vector2(right, 250f), new Vector2(600f, 50f));
            Place(NewImage(main, "Filet gauche", k_Amber).rectTransform, new Vector2(right - 270f, 250f), new Vector2(60f, 3f));
            Place(NewImage(main, "Filet droit", k_Amber).rectTransform, new Vector2(right + 270f, 250f), new Vector2(60f, 3f));

            menu.playButton = NewButton(main, "Jouer", "JOUER", new Vector2(right, 100f));
            menu.controlsButton = NewButton(main, "Commandes", "COMMANDES", new Vector2(right, -35f));
            menu.quitButton = NewButton(main, "Quitter", "QUITTER", new Vector2(right, -170f));

            var hint = NewText(main, "Aide", "Visez un bouton avec la manette\net appuyez sur la gâchette", 26, k_Dim);
            hint.alignment = TextAlignmentOptions.Center;
            Place(hint.rectTransform, new Vector2(right, -310f), new Vector2(600f, 80f));

            var controls = NewPage(page, "Page commandes");
            var heading = NewText(controls, "Titre", "COMMANDES", 40, k_Muted);
            heading.characterSpacing = 25f;
            heading.alignment = TextAlignmentOptions.Center;
            Place(heading.rectTransform, new Vector2(right, 280f), new Vector2(600f, 60f));
            var amberHex = ColorUtility.ToHtmlStringRGB(k_Amber);
            var help = NewText(controls, "Liste",
                $"<color=#{amberHex}>Joystick gauche</color>\nse déplacer\n\n" +
                $"<color=#{amberHex}>Joystick droit</color>\ntourner\n\n" +
                $"<color=#{amberHex}>Grip</color> (bouton latéral)\nattraper un objet\n\n" +
                $"<color=#{amberHex}>Gâchette</color>\ninteragir (boutons, digicode)", 30, k_Cream);
            help.alignment = TextAlignmentOptions.Center;
            Place(help.rectTransform, new Vector2(right, 30f), new Vector2(640f, 420f));
            menu.backButton = NewButton(controls, "Retour", "RETOUR", new Vector2(right, -300f));
            controls.gameObject.SetActive(false);

            menu.mainPage = main.gameObject;
            menu.controlsPage = controls.gameObject;
        }

        static RectTransform NewPage(Transform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            page.gameObject.layer = parent.gameObject.layer;
            page.SetParent(parent, false);
            Stretch(page);
            return page;
        }

        // Bouton à cadre ambré, dont le fond s'allume quand le rayon le survole.
        static Button NewButton(Transform parent, string name, string text, Vector2 position)
        {
            var size = new Vector2(420f, 100f);
            var frame = NewImage(parent, name, k_Amber);
            frame.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true; // c'est lui que le rayon vise
            Place(frame.rectTransform, position, size);

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.15f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.55f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.15f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.85f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Cadre fixe (4 traits) : reste visible, alors que le fond du bouton ne s'allume qu'au survol.
            foreach (var (pos, lineSize) in new[]
                     {
                         (new Vector2(0f, size.y / 2f), new Vector2(size.x, 3f)), (new Vector2(0f, -size.y / 2f), new Vector2(size.x, 3f)),
                         (new Vector2(-size.x / 2f, 0f), new Vector2(3f, size.y)), (new Vector2(size.x / 2f, 0f), new Vector2(3f, size.y)),
                     })
            {
                var line = NewImage(frame.transform, "Cadre", k_Amber);
                line.raycastTarget = false;
                Place(line.rectTransform, pos, lineSize);
            }

            var label = NewText(frame.transform, "Label", text, 46, k_Cream);
            label.characterSpacing = 12f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            Stretch(label.rectTransform);
            return button;
        }

        static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
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
    }
}
