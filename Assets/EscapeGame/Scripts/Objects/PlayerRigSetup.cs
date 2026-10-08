using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace EscapeGame
{
    /// <summary>
    /// Retire du joueur (XR Origin) des fonctions du modèle VR de Unity inutiles pour le jeu, dans chaque scène :
    /// - la téléportation (joystick droit vers l'avant) : le joystick droit ne sert plus qu'à tourner ;
    /// - le suivi des mains sans manettes (mains, permissions Android associées).
    /// Rien n'est modifié dans les scènes : c'est appliqué au lancement.
    /// </summary>
    static class PlayerRigSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded += (_, _) => Apply();
            Apply();
        }

        static void Apply()
        {
            if (Object.FindAnyObjectByType<XROrigin>() == null)
                return;
            RemoveTeleport();
            RemoveHandTracking();
        }

        static void RemoveTeleport()
        {
            foreach (var provider in Object.FindObjectsByType<TeleportationProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                provider.enabled = false;

            // Gestionnaire des manettes du modèle XR (Starter Assets) : on lui retire l'action "mode téléportation"
            // (joystick vers l'avant), qui sinon remplace le rayon par l'arc de téléportation.
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var type = behaviour.GetType();
                if (type.Name != "ControllerInputActionManager")
                    continue;

                var wasEnabled = behaviour.enabled;
                behaviour.enabled = false; // se désabonne des actions actuelles
                foreach (var fieldName in new[] { "m_TeleportMode", "m_TeleportModeCancel" })
                {
                    var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.GetValue(behaviour) is InputActionReference reference && reference != null)
                    {
                        reference.action?.Disable();
                        field.SetValue(behaviour, null);
                    }
                }
                var interactorField = type.GetField("m_TeleportInteractor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (interactorField?.GetValue(behaviour) is Component interactor && interactor != null)
                    interactor.gameObject.SetActive(false);
                behaviour.enabled = wasEnabled;
            }
        }

        static void RemoveHandTracking()
        {
            // Gestionnaire qui bascule entre manettes et mains : on ne garde que les manettes.
            foreach (var manager in Object.FindObjectsByType<XRInputModalityManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var hand in new[] { manager.leftHand, manager.rightHand })
                    if (hand != null)
                        hand.SetActive(false);
                manager.leftHand = null;
                manager.rightHand = null;
            }

            // Demande de permission Android pour le suivi des mains.
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (behaviour.GetType().Name == "PermissionsManager")
                    behaviour.gameObject.SetActive(false);
        }
    }
}
