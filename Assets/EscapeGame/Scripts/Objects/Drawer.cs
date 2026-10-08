using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EscapeGame
{
    /// <summary>
    /// Tiroir que l'on ouvre en attrapant sa façade et en tirant : il suit la main, mais seulement dans son axe,
    /// entre fermé et ouvert au maximum. Les objets posés dedans bougent avec lui.
    /// Verrouillé (cadenas), il tremble quand on tire. Se règle avec le menu "Rendre les tiroirs ouvrables".
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Drawer : MonoBehaviour
    {
        [Tooltip("Meuble qui sert de repère (le tiroir coulisse par rapport à lui).")]
        public Transform frame;
        [Tooltip("Position du tiroir fermé, dans le repère du meuble.")]
        public Vector3 closedPosition;
        [Tooltip("Direction d'ouverture, dans le repère du meuble.")]
        public Vector3 openDirection = Vector3.forward;
        [Tooltip("Ouverture maximale (m).")]
        public float maxOpen = 0.4f;
        [Tooltip("Ouverture au lancement du jeu (m). 0 = fermé.")]
        public float startOpen;

        [Header("Verrou")]
        public bool locked;
        [Tooltip("Amplitude (m) du tremblement quand on tire sur le tiroir verrouillé.")]
        public float rattleAmplitude = 0.004f;
        public UnityEvent onUnlocked = new UnityEvent();

        [Header("Contenu")]
        [Tooltip("Volume intérieur (repère de \"collisions\") : les objets qui s'y trouvent suivent le tiroir.")]
        public Transform contentSpace;
        public Vector3 contentCenter;
        public Vector3 contentSize = new Vector3(0.5f, 0.15f, 0.7f);

        [Header("Vibrations")]
        [Range(0f, 1f)] public float limitHaptic = 0.4f;
        [Range(0f, 1f)] public float rattleHaptic = 0.5f;

        /// <summary>Ouverture actuelle (m).</summary>
        public float OpenAmount { get; private set; }

        XRSimpleInteractable m_Interactable;
        IXRSelectInteractor m_Hand;
        Vector3 m_HandStart;
        float m_OpenAtGrab;
        bool m_AtLimit;
        Vector3 m_LastFixedPosition;
        readonly Collider[] m_Overlaps = new Collider[32];
        readonly HashSet<Rigidbody> m_Moved = new HashSet<Rigidbody>();
        Collider[] m_OwnColliders;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_OwnColliders = GetComponentsInChildren<Collider>(true);
            if (frame == null)
                frame = transform.parent;
            SetOpen(startOpen);
            m_LastFixedPosition = transform.position;
        }

        void OnEnable()
        {
            m_Interactable.selectEntered.AddListener(OnGrab);
            m_Interactable.selectExited.AddListener(OnRelease);
        }

        void OnDisable()
        {
            m_Interactable.selectEntered.RemoveListener(OnGrab);
            m_Interactable.selectExited.RemoveListener(OnRelease);
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            m_Hand = args.interactorObject;
            m_HandStart = HandPosition();
            m_OpenAtGrab = OpenAmount;
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject != m_Hand)
                return;
            m_Hand = null;
            if (locked)
                SetOpen(0f);
        }

        void Update()
        {
            if (m_Hand == null || frame == null)
                return;

            var axis = frame.TransformDirection(openDirection).normalized;
            var pull = Vector3.Dot(HandPosition() - m_HandStart, axis);

            if (locked)
            {
                // Bloqué : le tiroir tressaute un peu, plus fort si l'on tire fort.
                var strength = Mathf.Clamp01((pull - 0.01f) / 0.05f);
                SetOpen(strength * rattleAmplitude * (0.5f + 0.5f * Mathf.Sin(Time.time * 60f)));
                if (strength > 0f)
                    Vibrate(rattleHaptic * strength, Time.deltaTime * 2f);
                return;
            }

            var open = Mathf.Clamp(m_OpenAtGrab + pull, 0f, maxOpen);
            SetOpen(open);

            // Petit choc aux butées.
            var atLimit = open <= 0f || open >= maxOpen;
            if (atLimit && !m_AtLimit)
                Vibrate(limitHaptic, 0.05f);
            m_AtLimit = atLimit;
        }

        // Les objets posés dans le tiroir suivent son déplacement.
        void FixedUpdate()
        {
            var delta = transform.position - m_LastFixedPosition;
            m_LastFixedPosition = transform.position;
            if (delta.sqrMagnitude < 1e-10f || contentSpace == null)
                return;

            var center = contentSpace.TransformPoint(contentCenter) - delta; // volume à sa position d'avant
            var halfSize = Vector3.Scale(contentSize, contentSpace.lossyScale) / 2f;
            var count = Physics.OverlapBoxNonAlloc(center, halfSize, m_Overlaps, contentSpace.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            m_Moved.Clear();
            for (var i = 0; i < count; i++)
            {
                var body = m_Overlaps[i].attachedRigidbody;
                if (body == null || body.isKinematic || !m_Moved.Add(body) || IsOwnCollider(m_Overlaps[i]))
                    continue;
                var grab = body.GetComponent<IXRSelectInteractable>();
                if (grab != null && grab.isSelected)
                    continue; // tenu en main : ne pas l'arracher de la main
                body.position += delta;
                ReturnToStartOnFloor.NotifyCarried(body); // déplacé par le tiroir, pas un bug de traversée
            }
        }

        /// <summary>Déverrouille le tiroir (ex. cadenas ouvert, énigme résolue).</summary>
        public void Unlock()
        {
            if (!locked)
                return;
            locked = false;
            m_OpenAtGrab = OpenAmount;
            m_HandStart = m_Hand != null ? HandPosition() : m_HandStart;
            onUnlocked.Invoke();
        }

        public void Lock()
        {
            locked = true;
            SetOpen(0f);
        }

        void SetOpen(float open)
        {
            OpenAmount = open;
            if (frame != null)
                transform.position = frame.TransformPoint(closedPosition) + frame.TransformDirection(openDirection).normalized * open;
        }

        Vector3 HandPosition()
        {
            if (m_Hand == null)
                return Vector3.zero;
            var attach = m_Hand.GetAttachTransform(m_Interactable);
            return attach != null ? attach.position : m_Hand.transform.position;
        }

        void Vibrate(float amplitude, float duration)
        {
            if (amplitude > 0f && m_Hand is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }

        bool IsOwnCollider(Collider c)
        {
            foreach (var own in m_OwnColliders)
                if (own == c)
                    return true;
            return false;
        }

        void OnDrawGizmosSelected()
        {
            if (contentSpace == null)
                return;
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.6f);
            Gizmos.matrix = contentSpace.localToWorldMatrix;
            Gizmos.DrawWireCube(contentCenter, contentSize);
        }
    }
}
