using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeGame
{
    /// <summary>
    /// Puzzle 2D : le joueur fait glisser les pièces (JigsawPiece) de la réserve vers le cadre.
    /// Chaque pièce s'aimante à sa place quand elle en est assez proche ; quand toutes sont placées, l'énigme est résolue.
    /// Les places sont enregistrées dans l'éditeur (boutons de l'Inspector de ce composant).
    /// </summary>
    public class JigsawPuzzle : Puzzle
    {
        [Header("Zones")]
        [Tooltip("Cadre où l'on assemble le puzzle. Les places des pièces sont mémorisées par rapport à lui.")]
        public RectTransform board;
        [Tooltip("Réserve où les pièces sont rangées au départ.")]
        public RectTransform tray;
        public JigsawPiece[] pieces = new JigsawPiece[0];

        [Header("Aimantation")]
        [Tooltip("Distance (unités UI) sous laquelle une pièce lâchée s'aimante à sa place. 50 = 2,5 cm sur l'écran par défaut.")]
        public float snapTolerance = 50f;

        [Header("Affichage")]
        [Tooltip("Bords du cadre, colorés à la réussite.")]
        public Graphic[] frameGraphics = new Graphic[0];
        public Color solvedFrameColor = new Color(0.3f, 0.9f, 0.4f, 1f);
        public TMP_Text feedback;
        public string successMessage = "Puzzle terminé !";
        [Tooltip("Message affiché à chaque pièce placée. {0} = pièces placées, {1} = total.")]
        public string progressMessage = "Pièces placées : {0} / {1}";
        [Tooltip("Image de la solution affichée dans le cadre pour aider à placer les pièces dans l'éditeur. Cachée en jeu.")]
        public GameObject editorGuide;

        Color[] m_FrameColors;

        /// <summary>Point du cadre (par rapport à son centre) vers le monde.</summary>
        public Vector3 BoardToWorld(Vector2 boardPoint) => board.TransformPoint(board.rect.center + boardPoint);

        /// <summary>Point du monde vers le cadre (par rapport à son centre).</summary>
        public Vector2 WorldToBoard(Vector3 worldPoint) => (Vector2)board.InverseTransformPoint(worldPoint) - board.rect.center;

        void Awake()
        {
            m_FrameColors = new Color[frameGraphics.Length];
            for (var i = 0; i < frameGraphics.Length; i++)
                if (frameGraphics[i] != null)
                    m_FrameColors[i] = frameGraphics[i].color;

            if (editorGuide != null)
                editorGuide.SetActive(false);
            foreach (var piece in pieces)
                if (piece != null)
                    piece.Initialize(this);
        }

        void Start()
        {
            if (feedback != null)
                feedback.text = "";
        }

        internal void OnPiecePlaced()
        {
            var placed = 0;
            foreach (var piece in pieces)
                if (piece == null || piece.IsPlaced)
                    placed++;

            if (placed == pieces.Length)
            {
                Solve();
                return;
            }

            if (feedback != null)
                feedback.text = string.Format(progressMessage, placed, pieces.Length);
        }

        public override void ResetPuzzle()
        {
            base.ResetPuzzle();
            foreach (var piece in pieces)
                if (piece != null)
                    piece.ReturnToStart();
            for (var i = 0; i < frameGraphics.Length; i++)
                if (frameGraphics[i] != null)
                    frameGraphics[i].color = m_FrameColors[i];
            if (feedback != null)
                feedback.text = "";
        }

        protected override void OnSolved()
        {
            foreach (var graphic in frameGraphics)
                if (graphic != null)
                    graphic.color = solvedFrameColor;
            if (feedback != null)
            {
                feedback.text = successMessage;
                feedback.color = solvedFrameColor;
            }
        }
    }
}
