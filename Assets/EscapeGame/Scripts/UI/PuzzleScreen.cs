using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame
{
    /// <summary>
    /// Écran d'énigme flottant (Canvas en World Space). Apparaît en fondu quand le joueur s'approche
    /// et reste utilisable avec les rayons des manettes ou le doigt (poke).
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public class PuzzleScreen : MonoBehaviour
    {
        [Header("Apparition")]
        [Tooltip("Distance (m) entre la tête du joueur et l'écran pour qu'il apparaisse. 0 = toujours visible.")]
        public float showDistance = 3f;
        [Tooltip("Marge pour éviter le clignotement quand le joueur est pile à la limite.")]
        public float hideMargin = 0.5f;
        public float fadeDuration = 0.3f;

        [Header("Comportement")]
        [Tooltip("L'écran se tourne vers le joueur quand il apparaît (rotation horizontale seulement).")]
        public bool facePlayerOnShow = false;
        [Tooltip("Cache l'écran une fois l'énigme résolue.")]
        public bool hideWhenSolved = false;
        [Tooltip("Délai avant de cacher l'écran résolu (le temps de lire le message de réussite).")]
        public float hideDelay = 2f;

        CanvasGroup m_Group;
        Puzzle m_Puzzle;
        Transform m_Head;
        bool m_Visible;
        float m_SolvedTime = -1f;
        bool? m_Forced;

        public bool IsVisible => m_Visible;

        void Awake()
        {
            m_Group = GetComponent<CanvasGroup>();
            GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            m_Puzzle = GetComponentInChildren<Puzzle>(true);
            if (m_Puzzle != null)
                m_Puzzle.onSolved.AddListener(() => m_SolvedTime = Time.time);

            m_Visible = showDistance <= 0f;
            m_Group.alpha = m_Visible ? 1f : 0f;
            ApplyInteractable();
        }

        void Update()
        {
            if (m_Head == null && Camera.main != null)
                m_Head = Camera.main.transform;

            var shouldShow = ShouldShow();
            if (shouldShow != m_Visible)
            {
                m_Visible = shouldShow;
                if (m_Visible && facePlayerOnShow)
                    FacePlayer();
            }

            var speed = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, m_Visible ? 1f : 0f, speed);
            ApplyInteractable();
        }

        bool ShouldShow()
        {
            if (m_Forced.HasValue)
                return m_Forced.Value;
            if (hideWhenSolved && m_SolvedTime >= 0f && Time.time - m_SolvedTime >= hideDelay)
                return false;
            if (showDistance <= 0f)
                return true;
            if (m_Head == null)
                return false;

            var distance = Vector3.Distance(m_Head.position, transform.position);
            return m_Visible ? distance <= showDistance + hideMargin : distance <= showDistance;
        }

        void ApplyInteractable()
        {
            var usable = m_Visible && m_Group.alpha > 0.5f;
            m_Group.interactable = usable;
            m_Group.blocksRaycasts = usable;
        }

        public void FacePlayer()
        {
            if (m_Head == null)
                return;
            var away = transform.position - m_Head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(away);
        }

        /// <summary>Force l'affichage, quelle que soit la distance (ex. depuis un événement).</summary>
        public void Show() => m_Forced = true;

        /// <summary>Force le masquage, quelle que soit la distance.</summary>
        public void Hide() => m_Forced = false;

        /// <summary>Revient au comportement automatique (selon la distance).</summary>
        public void UseDistance() => m_Forced = null;

        void OnDrawGizmosSelected()
        {
            if (showDistance <= 0f)
                return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, showDistance);
        }
    }
}
