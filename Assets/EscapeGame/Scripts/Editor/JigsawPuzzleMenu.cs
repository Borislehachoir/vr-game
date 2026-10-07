using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menu "GameObject > Escape Game > Écran d'énigme - Puzzle" : écran avec la réserve de pièces à gauche
    /// et le cadre à droite. Les pièces sont les PNG du dossier Assets/Enigmes/Puzzle.
    /// </summary>
    static class JigsawPuzzleMenu
    {
        // Pièces redécoupées dans l'image solution (elles s'emboîtent exactement) et leur place.
        // Les PNG d'origine restent dans Assets/Enigmes/Puzzle.
        const string k_PiecesFolder = "Assets/Enigmes/Puzzle ajuste";
        const string k_SolutionImagePath = "Assets/Enigmes/Solution puzzle.png";
        const string k_SolutionLayoutPath = k_PiecesFolder + "/layout.csv";
        const float k_MetersPerUnit = 0.0005f; // 1 unité UI = 0,5 mm
        const float k_SolutionScale = 2f;     // 1 pixel de l'image solution = 2 unités UI (= 1 mm)
        const float k_Margin = 60f;
        const float k_Gap = 80f;
        const float k_TrayWidth = 1800f;
        static readonly Vector2 k_DefaultBoardSize = new Vector2(1300f, 1300f);
        const float k_FrameThickness = 10f;

        /// <summary>Place de chaque pièce lue dans le CSV de la solution (en pixels de l'image solution).</summary>
        class SolutionLayout
        {
            public Vector2 imageSize;
            public readonly Dictionary<string, Rect> pieces = new Dictionary<string, Rect>();
        }

        [MenuItem("GameObject/Escape Game/Écran d'énigme - Puzzle", false, 12)]
        static void CreatePuzzleScreen(MenuCommand command)
        {
            var sprites = LoadPieceSprites();
            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("Puzzle", $"Aucun PNG trouvé dans {k_PiecesFolder}.", "OK");
                return;
            }

            PuzzleScreenMenu.EnsureXREventSystem();

            var layout = LoadSolutionLayout();
            var boardSize = layout != null ? layout.imageSize * k_SolutionScale : k_DefaultBoardSize;
            var traySize = new Vector2(k_TrayWidth, boardSize.y);
            var width = k_Margin * 2f + traySize.x + k_Gap + boardSize.x;
            var height = k_Margin + traySize.y + 140f;
            var root = PuzzleScreenMenu.CreateScreenRoot("Puzzle", command, new Vector2(width, height));
            root.transform.localScale = Vector3.one * k_MetersPerUnit;
            root.GetComponent<PuzzleScreen>().showDistance = 4f;
            PuzzleScreenMenu.CreatePanelBackground(root.transform);

            var puzzle = root.AddComponent<JigsawPuzzle>();
            puzzle.puzzleId = root.name;

            // Réserve (gauche) et cadre (droite).
            var trayX = -width / 2f + k_Margin + traySize.x / 2f;
            var boardX = width / 2f - k_Margin - boardSize.x / 2f;
            puzzle.tray = PuzzleScreenMenu.CreateImage(root.transform, "Réserve", new Color(1f, 1f, 1f, 0.05f),
                new Vector2(trayX, -k_Margin), traySize).rectTransform;
            var board = PuzzleScreenMenu.CreateImage(root.transform, "Cadre", new Color(1f, 1f, 1f, 0.04f),
                new Vector2(boardX, -k_Margin), boardSize);
            puzzle.board = board.rectTransform;
            puzzle.frameGraphics = CreateFrameBorders(board.rectTransform);
            puzzle.editorGuide = CreateGuide(board.rectTransform);

            puzzle.feedback = PuzzleScreenMenu.CreateText(root.transform, "Message", "", 60, FontStyles.Bold,
                new Vector2(boardX, -k_Margin - traySize.y - 20f), new Vector2(boardSize.x, 90f));

            // Calque des pièces : couvre tout l'écran pour qu'elles passent de la réserve au cadre.
            var layer = new GameObject("Pièces", typeof(RectTransform));
            layer.layer = root.layer;
            layer.transform.SetParent(root.transform, false);
            PuzzleScreenMenu.Stretch((RectTransform)layer.transform);

            puzzle.pieces = sprites.Select(sprite => CreatePiece(layer.transform, sprite)).ToArray();
            var missing = layout != null ? ApplySolutionLayout(puzzle, layout) : new List<string>();
            JigsawPuzzleEditor.ArrangeInTray(puzzle);

            Undo.RegisterCreatedObjectUndo(root, "Créer écran puzzle");
            Selection.activeGameObject = root;

            if (layout == null)
                EditorUtility.DisplayDialog("Puzzle", $"Pas de solution trouvée ({k_SolutionLayoutPath}) : " +
                    "assemble le puzzle dans le cadre puis utilise « Enregistrer la solution » dans l'Inspector.", "OK");
            else if (missing.Count > 0)
                EditorUtility.DisplayDialog("Puzzle", "Pièces absentes de la solution (à placer à la main) : " +
                    string.Join(", ", missing), "OK");
        }

        static SolutionLayout LoadSolutionLayout()
        {
            var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(k_SolutionLayoutPath);
            if (csv == null)
                return null;

            var layout = new SolutionLayout();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var rawLine in csv.text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var cells = line.Split(';');
                if (cells[0] == "image" && cells.Length >= 3)
                    layout.imageSize = new Vector2(float.Parse(cells[1], culture), float.Parse(cells[2], culture));
                else if (cells.Length >= 5 && float.TryParse(cells[1], System.Globalization.NumberStyles.Float, culture, out var x))
                    layout.pieces[cells[0]] = new Rect(x, float.Parse(cells[2], culture),
                        float.Parse(cells[3], culture), float.Parse(cells[4], culture));
            }
            return layout.imageSize != Vector2.zero ? layout : null;
        }

        /// <summary>
        /// Donne à chaque pièce la taille et la place qu'elle a dans l'image solution
        /// (les PNG n'ont pas tous été exportés à la même échelle). Renvoie les pièces absentes du CSV.
        /// </summary>
        static List<string> ApplySolutionLayout(JigsawPuzzle puzzle, SolutionLayout layout)
        {
            var missing = new List<string>();
            foreach (var piece in puzzle.pieces)
            {
                var spriteName = piece.GetComponent<Image>().sprite.name;
                if (!layout.pieces.TryGetValue(spriteName, out var rect))
                {
                    missing.Add(spriteName);
                    continue;
                }

                ((RectTransform)piece.transform).sizeDelta = rect.size * k_SolutionScale;
                // Image : origine en haut à gauche, y vers le bas. Cadre : origine au centre, y vers le haut.
                piece.targetInBoard = new Vector2(
                    (rect.center.x - layout.imageSize.x / 2f) * k_SolutionScale,
                    (layout.imageSize.y / 2f - rect.center.y) * k_SolutionScale);
                piece.hasTarget = true;
            }

            var guideSprite = LoadSprite(k_SolutionImagePath, false);
            if (guideSprite != null && puzzle.editorGuide != null)
                puzzle.editorGuide.GetComponent<Image>().sprite = guideSprite;
            return missing;
        }

        static Sprite LoadSprite(string path, bool readable)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return null;
            if (importer.textureType != TextureImporterType.Sprite || importer.isReadable != readable)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.isReadable = readable;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static List<Sprite> LoadPieceSprites()
        {
            var sprites = new List<Sprite>();
            if (!AssetDatabase.IsValidFolder(k_PiecesFolder))
                return sprites;

            var paths = Directory.GetFiles(k_PiecesFolder, "*.png")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out var n) ? n : int.MaxValue)
                .ThenBy(p => p);

            foreach (var path in paths)
            {
                ConfigurePieceTexture(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    sprites.Add(sprite);
            }
            return sprites;
        }

        // Les PNG doivent être des Sprites lisibles (pour ne cliquer que sur la partie visible de la pièce).
        static void ConfigurePieceTexture(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;

            var changed = importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single
                || !importer.isReadable
                || !importer.alphaIsTransparency
                || importer.mipmapEnabled;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            changed |= settings.spriteMeshType != SpriteMeshType.FullRect;
            if (!changed)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        static JigsawPiece CreatePiece(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Pièce " + sprite.name, typeof(RectTransform), typeof(Image), typeof(JigsawPiece));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.SetNativeSize(); // 1 pixel du PNG = 1 unité : toutes les pièces gardent la même échelle
            return go.GetComponent<JigsawPiece>();
        }

        static Graphic[] CreateFrameBorders(RectTransform board)
        {
            var borders = new Graphic[4];
            var specs = new[]
            {
                ("Bord haut", new Vector2(0f, 1f), new Vector2(1f, 1f)),
                ("Bord bas", new Vector2(0f, 0f), new Vector2(1f, 0f)),
                ("Bord gauche", new Vector2(0f, 0f), new Vector2(0f, 1f)),
                ("Bord droit", new Vector2(1f, 0f), new Vector2(1f, 1f)),
            };
            for (var i = 0; i < specs.Length; i++)
            {
                var (name, min, max) = specs[i];
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.layer = board.gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(board, false);
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                // Bord centré sur la limite du cadre : épaisseur fixe, et longueur du côté + épaisseur pour fermer les coins.
                rect.sizeDelta = new Vector2(k_FrameThickness, k_FrameThickness);
                var image = go.GetComponent<Image>();
                image.color = PuzzleScreenMenu.k_TextColor;
                image.raycastTarget = false;
                borders[i] = image;
            }
            return borders;
        }

        static GameObject CreateGuide(RectTransform board)
        {
            var guide = new GameObject("Guide solution (éditeur uniquement)", typeof(RectTransform), typeof(Image));
            guide.layer = board.gameObject.layer;
            var rect = (RectTransform)guide.transform;
            rect.SetParent(board, false);
            PuzzleScreenMenu.Stretch(rect);
            var image = guide.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.35f);
            image.preserveAspect = true;
            image.raycastTarget = false;
            guide.SetActive(false);
            return guide;
        }
    }

    /// <summary>
    /// Inspector du puzzle : boutons pour enregistrer la solution et ranger les pièces dans la réserve.
    /// </summary>
    [CustomEditor(typeof(JigsawPuzzle))]
    class JigsawPuzzleEditor : UnityEditor.Editor
    {
        const float k_TrayPadding = 25f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var puzzle = (JigsawPuzzle)target;
            var pieces = puzzle.pieces.Where(p => p != null).ToArray();
            var withTarget = pieces.Count(p => p.hasTarget);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Solution", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Places enregistrées : {withTarget} / {pieces.Length}.\n\n" +
                "1. Assemble les pièces dans le cadre (vue Scene), en t'aidant si besoin du « Guide solution ».\n" +
                "2. Clique sur « Enregistrer la solution ».\n" +
                "3. Clique sur « Ranger les pièces dans la réserve » : c'est la position de départ en jeu.",
                withTarget == pieces.Length ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(puzzle.board == null || Application.isPlaying))
            {
                if (GUILayout.Button("1. Enregistrer la solution (pièces dans le cadre)"))
                    SaveSolution(puzzle);
                if (GUILayout.Button("2. Ranger les pièces dans la réserve"))
                    ArrangeInTray(puzzle);
                using (new EditorGUI.DisabledScope(withTarget == 0))
                    if (GUILayout.Button("Replacer les pièces sur la solution (pour vérifier / corriger)"))
                        ShowSolution(puzzle);
            }
        }

        static void SaveSolution(JigsawPuzzle puzzle)
        {
            var outside = new List<string>();
            foreach (var piece in puzzle.pieces.Where(p => p != null))
            {
                Undo.RecordObject(piece, "Enregistrer la solution");
                piece.targetInBoard = puzzle.WorldToBoard(piece.transform.position);
                piece.hasTarget = true;
                if (!puzzle.board.rect.Contains(puzzle.board.rect.center + piece.targetInBoard))
                    outside.Add(piece.name);
                EditorUtility.SetDirty(piece);
            }

            if (outside.Count > 0)
                EditorUtility.DisplayDialog("Puzzle",
                    "Solution enregistrée, mais ces pièces ont leur centre hors du cadre :\n" + string.Join(", ", outside), "OK");
        }

        static void ShowSolution(JigsawPuzzle puzzle)
        {
            foreach (var piece in puzzle.pieces.Where(p => p != null && p.hasTarget))
            {
                Undo.RecordObject(piece.transform, "Replacer sur la solution");
                piece.transform.position = puzzle.BoardToWorld(piece.targetInBoard);
            }
        }

        /// <summary>
        /// Range les pièces en rangées dans la réserve (les plus hautes d'abord), dans un ordre mélangé
        /// par rapport à la solution. S'il n'y a plus de place, les rangées suivantes se superposent en tas.
        /// </summary>
        internal static void ArrangeInTray(JigsawPuzzle puzzle)
        {
            if (puzzle.tray == null)
                return;

            var tray = puzzle.tray.rect;
            var pieces = puzzle.pieces.Where(p => p != null)
                .OrderByDescending(p => ((RectTransform)p.transform).rect.height)
                .ToList();

            var x = tray.xMin + k_TrayPadding;
            var top = tray.yMax - k_TrayPadding;
            var rowHeight = 0f;
            var pass = 0;
            foreach (var piece in pieces)
            {
                var size = ((RectTransform)piece.transform).rect.size;
                if (x + size.x > tray.xMax - k_TrayPadding && x > tray.xMin + k_TrayPadding)
                {
                    x = tray.xMin + k_TrayPadding;
                    top -= rowHeight + k_TrayPadding;
                    rowHeight = 0f;
                }
                if (top - size.y < tray.yMin + k_TrayPadding)
                {
                    // Plus de place : on recommence en haut, décalé, en tas par-dessus.
                    pass++;
                    x = tray.xMin + k_TrayPadding + pass * 60f;
                    top = tray.yMax - k_TrayPadding - pass * 60f;
                }

                var center = new Vector2(x + size.x / 2f, top - size.y / 2f);
                Undo.RecordObject(piece.transform, "Ranger les pièces");
                piece.transform.position = puzzle.tray.TransformPoint(center);
                piece.transform.SetAsLastSibling();

                x += size.x + k_TrayPadding;
                rowHeight = Mathf.Max(rowHeight, size.y);
            }
        }
    }
}
