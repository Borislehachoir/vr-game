using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame
{
    /// <summary>
    /// Renvoie un objet à sa position de départ (celle du lancement du jeu) quand :
    /// - il est resté au sol, sans être tenu, pendant quelques secondes ;
    /// - il est passé sous le sol ;
    /// - il a traversé un mur, un meuble ou le sol en tombant ou en étant lancé ;
    /// - il a été lâché dans un mur ou derrière un mur.
    /// Chaque retour est signalé dans la Console. Ajouté automatiquement à tous les objets attrapables
    /// (pour l'exclure d'un objet, ajouter ce composant à la main et le désactiver).
    /// </summary>
    public class ReturnToStartOnFloor : MonoBehaviour
    {
        [Tooltip("Temps (s) passé au sol avant que l'objet ne revienne à sa place.")]
        [Range(1f, 30f)] public float delay = 7f;
        [Tooltip("Hauteur (m) au-dessus du sol sous laquelle on considère que l'objet est au sol.")]
        public float floorMargin = 0.05f;
        [Tooltip("Revenir aussi quand l'objet traverse un mur ou un meuble, ou est lâché dedans / derrière.")]
        public bool returnWhenGlitched = true;
        [Tooltip("Écrire un message dans la Console à chaque retour.")]
        public bool logReturns = true;

        // Si l'objet passe à travers le sol, il revient tout de suite.
        const float k_FellThroughDepth = 0.5f;
        const float k_FloorSearchDistance = 20f;
        // Déplacement (m) en un pas de physique au-delà duquel on vérifie qu'aucun décor n'a été traversé.
        const float k_FastMove = 0.02f;

        static readonly Dictionary<Rigidbody, float> s_CarriedAt = new Dictionary<Rigidbody, float>();

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Collider[] m_Colliders;
        Vector3 m_StartPosition;
        Quaternion m_StartRotation;
        Vector3 m_LastPosition;
        float m_FloorHeight;
        float m_TimeOnFloor;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
            m_Colliders = GetComponentsInChildren<Collider>();
            m_StartPosition = transform.position;
            m_StartRotation = transform.rotation;
            m_LastPosition = GetCenter();
        }

        void OnEnable()
        {
            if (m_Grab != null)
                m_Grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            if (m_Grab != null)
                m_Grab.selectExited.RemoveListener(OnReleased);
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
                ReturnToStart("passé sous le sol");
                return;
            }

            if (bottom > m_FloorHeight + floorMargin)
            {
                m_TimeOnFloor = 0f;
                return;
            }

            m_TimeOnFloor += Time.deltaTime;
            if (m_TimeOnFloor >= delay)
                ReturnToStart($"resté au sol pendant {delay:0} s");
        }

        // Traversée d'un décor entre deux pas de physique (objet qui tombe ou qui est lancé trop vite).
        // Seulement pour un déplacement rapide : un objet posé qui bouge à peine n'est jamais concerné.
        void FixedUpdate()
        {
            var position = GetCenter();
            var held = m_Grab != null && m_Grab.isSelected;
            var free = !held && (m_Body == null || !m_Body.isKinematic) && !IsCarried(m_Body);

            if (returnWhenGlitched && free && (position - m_LastPosition).sqrMagnitude > k_FastMove * k_FastMove &&
                TryFindStaticHit(m_LastPosition, position, out var hit))
            {
                ReturnToStart($"a traversé « {hit.collider.name} »");
                return;
            }
            m_LastPosition = position;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (!returnWhenGlitched)
                return;
            var center = GetCenter();

            // Lâché dans un mur ou un meuble.
            foreach (var c in Physics.OverlapSphere(center, 0.0005f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (IsStaticScenery(c))
                {
                    ReturnToStart($"lâché dans « {c.name} »");
                    return;
                }

            // Lâché derrière un mur (la main est passée à travers) : un mur sépare la tête du joueur de l'objet.
            var head = Camera.main != null ? Camera.main.transform.position : center;
            foreach (var h in Physics.RaycastAll(head, center - head, Vector3.Distance(head, center),
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (IsStaticScenery(h.collider) && Mathf.Abs(h.normal.y) < 0.5f) // surface verticale = mur
                {
                    ReturnToStart($"lâché derrière « {h.collider.name} »");
                    return;
                }

            m_LastPosition = center;
        }

        /// <summary>Remet immédiatement l'objet à sa position de départ, immobile.</summary>
        public void ReturnToStart() => ReturnToStart(null);

        void ReturnToStart(string reason)
        {
            if (logReturns && reason != null)
                Debug.Log($"[Retour à sa place] « {name} » : {reason}.", this);

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
            Physics.SyncTransforms();
            m_LastPosition = GetCenter();
        }

        /// <summary>Signale qu'un objet est déplacé volontairement par le décor (ex. tiroir) : pas un bug de traversée.</summary>
        public static void NotifyCarried(Rigidbody body)
        {
            if (body != null)
                s_CarriedAt[body] = Time.fixedTime;
        }

        static bool IsCarried(Rigidbody body) =>
            body != null && s_CarriedAt.TryGetValue(body, out var time) && Time.fixedTime - time <= Time.fixedDeltaTime * 3f;

        bool TryFindStaticHit(Vector3 from, Vector3 to, out RaycastHit result)
        {
            result = default;
            var delta = to - from;
            if (delta.sqrMagnitude < 1e-6f)
                return false;
            foreach (var h in Physics.RaycastAll(from, delta, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (IsStaticScenery(h.collider))
                {
                    result = h;
                    return true;
                }
            return false;
        }

        // Décor fixe : collider sans Rigidbody (murs, sol, meubles), qui n'appartient pas à l'objet.
        bool IsStaticScenery(Collider c) =>
            c != null && !c.isTrigger && c.attachedRigidbody == null && !c.transform.IsChildOf(transform);

        Vector3 GetCenter()
        {
            var has = false;
            var bounds = new Bounds();
            foreach (var c in m_Colliders)
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;
                if (has)
                    bounds.Encapsulate(c.bounds);
                else
                    bounds = c.bounds;
                has = true;
            }
            return has ? bounds.center : transform.position;
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

        // Sol = là où se tiennent les pieds du joueur (XR Origin). Sinon : la surface la plus basse sous l'objet.
        float FindFloorHeight()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
                return origin.transform.position.y;

            var lowest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(m_StartPosition, Vector3.down, k_FloorSearchDistance,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform))
                    lowest = Mathf.Min(lowest, hit.point.y);

            if (lowest != float.MaxValue)
                return lowest;

            Debug.LogWarning($"{name} : aucun sol trouvé sous la position de départ. " +
                "L'objet reviendra s'il descend d'un mètre sous sa position de départ.", this);
            return m_StartPosition.y - 1f;
        }

        // Ajout automatique sur tous les objets attrapables de chaque scène chargée.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToAllGrabbables()
        {
            SceneManager.sceneLoaded += (_, _) => AddToScene();
            AddToScene();
        }

        static void AddToScene()
        {
            foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Le cadenas tombe exprès une fois ouvert : il ne doit pas remonter sur le tiroir.
                if (grab.GetComponent<ReturnToStartOnFloor>() != null || grab.GetComponent<KeyLock>() != null)
                    continue;
                grab.gameObject.AddComponent<ReturnToStartOnFloor>();
            }
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
