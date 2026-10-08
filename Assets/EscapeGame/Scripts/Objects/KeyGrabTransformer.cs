using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace EscapeGame
{
    /// <summary>
    /// Prise en main d'une LockKey. Clé libre : prise normale. Clé enfoncée dans un cadenas : elle reste dans la serrure,
    /// et seule la rotation du poignet autour de l'axe de la serrure la fait tourner.
    /// Si on tire la main en arrière (clé pas tournée), la clé ressort. Ajouté automatiquement par LockKey.
    /// </summary>
    public class KeyGrabTransformer : XRGeneralGrabTransformer
    {
        [Tooltip("Recul de la main (m) qui fait ressortir la clé de la serrure.")]
        public float pullOutDistance = 0.08f;

        LockKey m_Key;
        Vector3 m_HandUpAtStart;
        float m_AngleAtStart;
        float m_LastTick;
        KeyLock m_TrackedLock;

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            m_TrackedLock = null; // la référence de rotation est reprise au prochain Process
        }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose, ref Vector3 localScale)
        {
            if (m_Key == null)
                m_Key = GetComponent<LockKey>();

            var keyLock = m_Key != null ? m_Key.InsertedIn : null;
            if (keyLock == null || keyLock.keyhole == null || grabInteractable.interactorsSelecting.Count == 0)
            {
                m_TrackedLock = null;
                base.Process(grabInteractable, updatePhase, ref targetPose, ref localScale);
                return;
            }

            var keyhole = keyLock.keyhole;
            var axis = keyhole.forward;
            var hand = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);

            // Référence de rotation : prise à l'insertion ou à chaque nouvelle prise.
            if (m_TrackedLock != keyLock)
            {
                m_TrackedLock = keyLock;
                m_HandUpAtStart = Vector3.ProjectOnPlane(hand.up, axis);
                m_AngleAtStart = m_Key.TurnAngle;
                m_LastTick = m_Key.TurnAngle;
            }

            // Tirer la main en arrière ressort la clé (seulement si elle n'est presque pas tournée).
            var back = Vector3.Dot(keyhole.position - hand.position, axis);
            if (back > pullOutDistance && Mathf.Abs(m_Key.TurnAngle) < 10f)
            {
                keyLock.Release(m_Key);
                m_TrackedLock = null;
                base.Process(grabInteractable, updatePhase, ref targetPose, ref localScale);
                return;
            }

            // Rotation du poignet autour de l'axe de la serrure, dans un sens ou dans l'autre.
            var handUp = Vector3.ProjectOnPlane(hand.up, axis);
            var delta = handUp.sqrMagnitude > 1e-6f && m_HandUpAtStart.sqrMagnitude > 1e-6f
                ? Vector3.SignedAngle(m_HandUpAtStart, handUp, axis)
                : 0f;
            var angle = Mathf.Clamp(m_AngleAtStart + delta, -keyLock.turnAngle, keyLock.turnAngle);
            if (!keyLock.IsUnlocked)
                m_Key.TurnAngle = angle;

            // Petits crans en tournant.
            if (Mathf.Abs(m_Key.TurnAngle - m_LastTick) >= 15f)
            {
                m_LastTick = m_Key.TurnAngle;
                m_Key.Vibrate(0.15f, 0.02f);
            }

            targetPose = keyLock.GetKeyPose(m_Key, m_Key.TurnAngle);
            if (Mathf.Abs(m_Key.TurnAngle) >= keyLock.turnAngle - 1f)
                keyLock.Unlock();
        }
    }
}
