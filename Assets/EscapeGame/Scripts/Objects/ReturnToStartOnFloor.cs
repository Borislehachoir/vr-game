using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame
{
    /// <summary>
    /// Renvoie l'objet à sa position de départ quand il est resté au sol (sans être tenu) pendant quelques secondes.
    /// La position de départ est celle de l'objet au lancement du jeu.
    /// Le sol est trouvé automatiquement au lancement : c'est la surface la plus basse sous la position de départ.
    /// </summary>
    public class ReturnToStartOnFloor : MonoBehaviour
    {
        [Tooltip("Temps (s) passé au sol avant que l'objet ne revienne à sa place.")]
        [Range(1f, 30f)] public float delay = 7f;
        [Tooltip("Hauteur (m) au-dessus du sol sous laquelle on considère que l'objet est au sol.")]
        public float floorMargin = 0.05f;

        // Si l'objet passe à travers le sol, il revient tout de suite.
        const float k_FellThroughDepth = 0.5f;
        // Distance de recherche du sol sous la position de départ.
        const float k_FloorSearchDistance = 20f;

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Collider[] m_Colliders;
        Vector3 m_StartPosition;
        Quaternion m_StartRotation;
        float m_FloorHeight;
        float m_TimeOnFloor;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
            m_Colliders = GetComponentsInChildren<Collider>();
            m_StartPosition = transform.position;
            m_StartRotation = transform.rotation;
        }

        // Dans Start, une fois tous les colliders de la scène prêts.
        void Start() => m_FloorHeight = FindFloorHeight();

        void Update()
        {
            if (m_Grab != null && m_Grab.isSelected)
            {
                m_TimeOnFloor = 0f;
                return;
            }

            var bottom = GetBottom();
            if (bottom < m_FloorHeight - k_FellThroughDepth)
            {
                ReturnToStart();
                return;
            }

            if (bottom > m_FloorHeight + floorMargin)
            {
                m_TimeOnFloor = 0f;
                return;
            }

            m_TimeOnFloor += Time.deltaTime;
            if (m_TimeOnFloor >= delay)
                ReturnToStart();
        }

        /// <summary>Remet immédiatement l'objet à sa position de départ, immobile.</summary>
        public void ReturnToStart()
        {
            m_TimeOnFloor = 0f;
            transform.SetPositionAndRotation(m_StartPosition, m_StartRotation);
            if (m_Body != null)
            {
                m_Body.position = m_StartPosition;
                m_Body.rotation = m_StartRotation;
                if (!m_Body.isKinematic)
                {
                    m_Body.linearVelocity = Vector3.zero;
                    m_Body.angularVelocity = Vector3.zero;
                }
            }
        }

        // Point le plus bas de l'objet (bas de ses colliders, ou sa position s'il n'en a pas).
        float GetBottom()
        {
            var bottom = float.MaxValue;
            foreach (var c in m_Colliders)
                if (c != null && c.enabled && !c.isTrigger)
                    bottom = Mathf.Min(bottom, c.bounds.min.y);
            return bottom == float.MaxValue ? transform.position.y : bottom;
        }

        float FindFloorHeight()
        {
            var hits = Physics.RaycastAll(m_StartPosition, Vector3.down, k_FloorSearchDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            var lowest = float.MaxValue;
            foreach (var hit in hits)
                if (!hit.collider.transform.IsChildOf(transform))
                    lowest = Mathf.Min(lowest, hit.point.y);

            if (lowest != float.MaxValue)
                return lowest;

            Debug.LogWarning($"{name} : aucun sol trouvé sous la position de départ. " +
                "L'objet reviendra s'il descend d'un mètre sous sa position de départ.", this);
            return m_StartPosition.y - 1f;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(new Vector3(m_StartPosition.x, m_FloorHeight + floorMargin, m_StartPosition.z),
                new Vector3(1f, 0f, 1f));
        }
    }
}
