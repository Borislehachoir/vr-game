using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame
{
    /// <summary>
    /// Cadenas qui bloque un tiroir. On approche le bout de la bonne LockKey de la serrure : la clé s'enfonce,
    /// on la tourne d'un quart de tour, l'anse s'ouvre, le tiroir est libéré et le cadenas tombe.
    /// </summary>
    public class KeyLock : MonoBehaviour
    {
        [Tooltip("Identifiant de la clé acceptée (champ Key Id de la LockKey).")]
        public string keyId = "tiroir";
        [Tooltip("Entrée de la serrure. Son axe Z (bleu) pointe vers l'intérieur du cadenas, son axe Y (vert) vers le haut de la clé.")]
        public Transform keyhole;
        [Tooltip("Tiroir bloqué par ce cadenas.")]
        public Drawer drawer;
        [Tooltip("Anse du cadenas, soulevée à l'ouverture.")]
        public Transform shackle;

        [Header("Clé")]
        [Tooltip("Distance (m) entre le bout de la clé et la serrure pour qu'elle s'enfonce.")]
        public float captureRadius = 0.035f;
        [Tooltip("Profondeur (m) à laquelle la clé s'enfonce.")]
        public float insertDepth = 0.012f;
        [Tooltip("Angle (°) dont il faut tourner la clé pour ouvrir.")]
        public float turnAngle = 90f;

        [Header("Ouverture")]
        [Tooltip("Le cadenas (avec la clé) tombe après l'ouverture et peut être ramassé.")]
        public bool dropWhenUnlocked = true;
        public UnityEvent onUnlocked = new UnityEvent();

        public bool IsUnlocked { get; private set; }

        LockKey m_Inserted;
        Collider[] m_Colliders;

        void Awake() => m_Colliders = GetComponentsInChildren<Collider>(true);

        void Update()
        {
            if (IsUnlocked || m_Inserted != null || keyhole == null)
                return;

            foreach (var key in LockKey.All)
            {
                if (key.keyId != keyId || key.InsertedIn != null || !key.Grab.isSelected)
                    continue;
                // Bout de la clé dans la serrure, clé à peu près dans l'axe.
                if (Vector3.Distance(key.tip.position, keyhole.position) > captureRadius ||
                    Vector3.Dot(key.transform.forward, keyhole.forward) < 0.6f)
                    continue;

                m_Inserted = key;
                IgnoreCollisions(key, true);
                key.Insert(this);
                break;
            }
        }

        /// <summary>Pose de la clé enfoncée et tournée de <paramref name="angle"/> degrés.</summary>
        public Pose GetKeyPose(LockKey key, float angle)
        {
            var rotation = Quaternion.AngleAxis(angle, keyhole.forward) * Quaternion.LookRotation(keyhole.forward, keyhole.up);
            var tipOffset = Quaternion.Inverse(key.transform.rotation) * (key.tip.position - key.transform.position);
            var tipTarget = keyhole.position + keyhole.forward * insertDepth;
            return new Pose(tipTarget - rotation * tipOffset, rotation);
        }

        /// <summary>Retire la clé de la serrure (le joueur la ressort).</summary>
        public void Release(LockKey key)
        {
            if (key != m_Inserted)
                return;
            m_Inserted = null;
            key.Remove();
            IgnoreCollisions(key, false);
        }

        public void Unlock()
        {
            if (IsUnlocked)
                return;
            IsUnlocked = true;
            if (m_Inserted != null)
                m_Inserted.Vibrate(0.8f, 0.15f);
            StartCoroutine(OpenSequence());
        }

        IEnumerator OpenSequence()
        {
            // L'anse se soulève d'un coup sec.
            if (shackle != null)
            {
                var from = shackle.localPosition;
                var to = from + Vector3.up * 0.012f / Mathf.Max(transform.lossyScale.y, 1e-4f);
                for (var t = 0f; t < 0.12f; t += Time.deltaTime)
                {
                    shackle.localPosition = Vector3.Lerp(from, to, t / 0.12f);
                    yield return null;
                }
                shackle.localPosition = to;
            }

            if (drawer != null)
                drawer.Unlock();
            onUnlocked.Invoke();

            if (!dropWhenUnlocked)
                yield break;
            yield return new WaitForSeconds(0.6f);
            Drop();
        }

        // Le cadenas se détache du tiroir et tombe, la clé toujours dedans ; on peut ensuite le ramasser.
        void Drop()
        {
            if (m_Inserted != null)
            {
                var key = m_Inserted;
                if (key.Grab.isSelected && key.Grab.interactionManager != null)
                    key.Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)key.Grab);
                key.Grab.enabled = false;
                foreach (var c in key.GetComponentsInChildren<Collider>())
                    c.enabled = false;
                if (key.Body != null)
                    key.Body.isKinematic = true;
                key.transform.SetParent(transform, true);
            }

            transform.SetParent(null, true);
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.WakeUp();
            }
            var grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
                grab.enabled = true;
        }

        void IgnoreCollisions(LockKey key, bool ignore)
        {
            foreach (var keyCollider in key.GetComponentsInChildren<Collider>())
            foreach (var own in m_Colliders)
                if (keyCollider != null && own != null)
                    Physics.IgnoreCollision(keyCollider, own, ignore);
        }

        void OnDrawGizmosSelected()
        {
            if (keyhole == null)
                return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(keyhole.position, captureRadius);
            Gizmos.DrawRay(keyhole.position, keyhole.forward * 0.03f);
        }
    }
}
