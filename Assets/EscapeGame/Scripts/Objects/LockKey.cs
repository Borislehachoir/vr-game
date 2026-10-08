using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EscapeGame
{
    /// <summary>
    /// Clé à attraper puis enfoncer dans la serrure d'un KeyLock (cadenas) ; une fois enfoncée, on la tourne
    /// en tournant le poignet. Repère de la clé : axe Z (bleu) de l'anneau vers le bout, axe Y (vert) dans le plan du panneton.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class LockKey : MonoBehaviour
    {
        [Tooltip("Identifiant de la clé : un cadenas n'accepte que la clé portant le même identifiant.")]
        public string keyId = "tiroir";
        [Tooltip("Bout de la clé (ce qui entre dans la serrure).")]
        public Transform tip;

        static readonly List<LockKey> s_All = new List<LockKey>();

        /// <summary>Clés présentes dans la scène.</summary>
        public static IReadOnlyList<LockKey> All => s_All;

        /// <summary>Cadenas dans lequel la clé est enfoncée (null si elle est libre).</summary>
        public KeyLock InsertedIn { get; private set; }
        /// <summary>Angle actuel de la clé dans la serrure (°).</summary>
        public float TurnAngle { get; internal set; }

        internal XRGrabInteractable Grab { get; private set; }
        internal Rigidbody Body { get; private set; }
        XRBaseInteractable.MovementType m_FreeMovementType;

        void Awake()
        {
            Grab = GetComponent<XRGrabInteractable>();
            Body = GetComponent<Rigidbody>();
            m_FreeMovementType = Grab.movementType;
            if (tip == null)
                tip = transform;
            if (GetComponent<KeyGrabTransformer>() == null)
                gameObject.AddComponent<KeyGrabTransformer>();
        }

        void OnEnable()
        {
            s_All.Add(this);
            Grab.selectEntered.AddListener(OnGrabbed);
            Grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            s_All.Remove(this);
            Grab.selectEntered.RemoveListener(OnGrabbed);
            Grab.selectExited.RemoveListener(OnReleased);
        }

        float m_LastDropSound = -10f;

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (InsertedIn == null)
                GameSounds.Play(GameSounds.Bank?.keyGrab, transform.position);
        }

        // Bruit de clé quand elle tombe ou est posée (pas quand elle frôle quelque chose en main).
        void OnCollisionEnter(Collision collision)
        {
            if (Grab.isSelected || collision.relativeVelocity.magnitude < 0.5f || Time.time - m_LastDropSound < 0.3f)
                return;
            m_LastDropSound = Time.time;
            var volume = Mathf.Clamp01(collision.relativeVelocity.magnitude / 3f);
            GameSounds.Play(GameSounds.Bank?.keyDrop, transform.position, volume);
        }

        // Lâchée dans la serrure : elle y reste (sinon XRI la rendrait à la gravité).
        // Lâchée ailleurs : elle tombe, même si elle était immobilisée dans la serrure quand on l'a reprise.
        void OnReleased(SelectExitEventArgs args)
        {
            if (Body == null || !enabled)
                return;
            if (InsertedIn == null)
            {
                if (Grab.enabled)
                    Body.isKinematic = false;
            }
            else
            {
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
                Body.isKinematic = true;
                var pose = InsertedIn.GetKeyPose(this, TurnAngle);
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
        }

        internal void Insert(KeyLock keyLock)
        {
            InsertedIn = keyLock;
            TurnAngle = 0f;
            // Suit la main sans physique pendant qu'elle est dans la serrure (pas de tremblement contre le cadenas).
            Grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            Vibrate(0.3f, 0.05f);
            GameSounds.Play(GameSounds.Bank?.keyInsert, transform.position);
        }

        internal void Remove()
        {
            InsertedIn = null;
            TurnAngle = 0f;
            Grab.movementType = m_FreeMovementType;
            if (Body != null && !Grab.isSelected)
                Body.isKinematic = false;
        }

        internal void Vibrate(float amplitude, float duration)
        {
            foreach (var interactor in Grab.interactorsSelecting)
                if (interactor is XRBaseInputInteractor input)
                    input.SendHapticImpulse(amplitude, duration);
        }
    }
}
