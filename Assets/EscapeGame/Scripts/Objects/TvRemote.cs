using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EscapeGame
{
    /// <summary>
    /// Télécommande : quand on la tient, un appui sur un bouton de la manette qui la tient
    /// (A ou B à droite, X ou Y à gauche) allume ou éteint la télé.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class TvRemote : MonoBehaviour
    {
        [Tooltip("Télé commandée. Vide = la première télé (TvScreen) de la scène.")]
        public TvScreen tv;
        [Range(0f, 1f)] public float hapticAmplitude = 0.3f;

        XRGrabInteractable m_Grab;
        bool m_WasPressed;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            if (tv == null)
                tv = FindAnyObjectByType<TvScreen>();
        }

        void Update()
        {
            if (!m_Grab.isSelected)
            {
                m_WasPressed = false;
                return;
            }

            XRBaseInputInteractor pressingHand = null;
            foreach (var interactor in m_Grab.interactorsSelecting)
                if (IsButtonPressed(interactor))
                    pressingHand = interactor as XRBaseInputInteractor;

            var pressed = pressingHand != null;
            if (pressed && !m_WasPressed && tv != null)
            {
                tv.Toggle();
                if (hapticAmplitude > 0f)
                    pressingHand.SendHapticImpulse(hapticAmplitude, 0.05f);
            }
            m_WasPressed = pressed;
        }

        // A / B (main droite) ou X / Y (main gauche) : boutons "primaire" et "secondaire" de la manette.
        static bool IsButtonPressed(IXRInteractor interactor)
        {
            XRNode node;
            switch (interactor.handedness)
            {
                case InteractorHandedness.Left: node = XRNode.LeftHand; break;
                case InteractorHandedness.Right: node = XRNode.RightHand; break;
                default: return false;
            }

            var device = InputDevices.GetDeviceAtXRNode(node);
            return (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primary) && primary) ||
                   (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var secondary) && secondary);
        }
    }
}
