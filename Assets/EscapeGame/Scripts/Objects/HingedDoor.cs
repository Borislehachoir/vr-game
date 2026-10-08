using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EscapeGame
{
    /// <summary>
    /// Porte à charnière (armoire, placard...) : on l'attrape et elle pivote autour de sa charnière en suivant la main,
    /// entre fermée (0°) et grande ouverte. Cet objet est la charnière : la porte et sa poignée sont ses enfants.
    /// Verrouillée, elle tremble quand on tire. Se règle avec le menu "Rendre l'armoire ouvrable".
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class HingedDoor : MonoBehaviour
    {
        [Tooltip("Ouverture maximale (°).")]
        [Range(10f, 180f)] public float maxAngle = 110f;
        [Tooltip("Sens d'ouverture autour de l'axe vertical (+1 ou -1), calculé par le menu pour que la porte s'ouvre vers l'extérieur.")]
        public float openSign = 1f;
        [Tooltip("Ouverture au lancement (°).")]
        public float startAngle;

        [Header("Verrou")]
        public bool locked;
        [Tooltip("Amplitude (°) du tremblement quand on tire sur la porte verrouillée.")]
        public float rattleAngle = 1.5f;
        public UnityEvent onUnlocked = new UnityEvent();

        [Header("Vibrations")]
        [Range(0f, 1f)] public float limitHaptic = 0.3f;
        [Range(0f, 1f)] public float rattleHaptic = 0.5f;

        /// <summary>Ouverture actuelle (°).</summary>
        public float Angle { get; private set; }

        XRSimpleInteractable m_Interactable;
        IXRSelectInteractor m_Hand;
        Quaternion m_ClosedRotation;
        Vector3 m_HandAtGrab;
        float m_AngleAtGrab;
        bool m_AtLimit;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_ClosedRotation = transform.localRotation;
            SetAngle(startAngle);
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
            m_HandAtGrab = Flat(HandPosition());
            m_AngleAtGrab = Angle;
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject != m_Hand)
                return;
            m_Hand = null;
            if (locked)
                SetAngle(0f);
        }

        void Update()
        {
            if (m_Hand == null)
                return;

            // Angle dont la main a tourné autour de la charnière depuis la prise.
            var current = Flat(HandPosition());
            if (current.sqrMagnitude < 0.0025f || m_HandAtGrab.sqrMagnitude < 0.0025f)
                return; // main trop près de l'axe : direction instable
            var delta = Vector3.SignedAngle(m_HandAtGrab, current, Axis) * Mathf.Sign(openSign);

            if (locked)
            {
                var strength = Mathf.Clamp01((delta - 2f) / 10f);
                SetAngle(strength * rattleAngle * (0.5f + 0.5f * Mathf.Sin(Time.time * 60f)));
                if (strength > 0f)
                    Vibrate(rattleHaptic * strength, Time.deltaTime * 2f);
                return;
            }

            var angle = Mathf.Clamp(m_AngleAtGrab + delta, 0f, maxAngle);
            SetAngle(angle);

            var atLimit = angle <= 0f || angle >= maxAngle;
            if (atLimit && !m_AtLimit)
                Vibrate(limitHaptic, 0.05f);
            m_AtLimit = atLimit;
        }

        public void Unlock()
        {
            if (!locked)
                return;
            locked = false;
            m_AngleAtGrab = Angle;
            if (m_Hand != null)
                m_HandAtGrab = Flat(HandPosition());
            onUnlocked.Invoke();
        }

        public void Lock()
        {
            locked = true;
            SetAngle(0f);
        }

        void SetAngle(float angle)
        {
            Angle = angle;
            transform.localRotation = Quaternion.AngleAxis(Mathf.Sign(openSign) * angle, m_ClosedRotation * Vector3.up) * m_ClosedRotation;
        }

        // Axe de la charnière (vertical de la porte fermée), dans le monde.
        Vector3 Axis => transform.parent != null
            ? transform.parent.TransformDirection(m_ClosedRotation * Vector3.up).normalized
            : (m_ClosedRotation * Vector3.up).normalized;

        // Position de la main projetée sur le plan horizontal de la charnière, par rapport à la charnière.
        Vector3 Flat(Vector3 world) => Vector3.ProjectOnPlane(world - transform.position, Axis);

        Vector3 HandPosition()
        {
            var attach = m_Hand.GetAttachTransform(m_Interactable);
            return attach != null ? attach.position : m_Hand.transform.position;
        }

        void Vibrate(float amplitude, float duration)
        {
            if (amplitude > 0f && m_Hand is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            var axis = transform.up * 0.5f;
            Gizmos.DrawLine(transform.position - axis, transform.position + axis);
        }
    }
}
