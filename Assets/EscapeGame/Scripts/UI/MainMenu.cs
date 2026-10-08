using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace EscapeGame
{
    /// <summary>
    /// Menu principal affiché sur l'écran de la télé. Au lancement, le joueur est assis sur le canapé,
    /// face à la télé : il ne peut que tourner la tête (déplacements et rotations désactivés).
    /// "Jouer" éteint l'écran, pose le joueur debout devant le canapé et lui rend ses déplacements.
    /// Créé par le menu "Escape Game > Menu > Créer le menu sur la télé".
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Header("Écran")]
        public CanvasGroup screen;
        public GameObject mainPage;
        public GameObject controlsPage;

        [Header("Joueur")]
        [Tooltip("Laisser vide : trouvé automatiquement dans la scène.")]
        public XROrigin origin;
        [Tooltip("Position des yeux du joueur assis sur le canapé.")]
        public Transform seatEyes;
        [Tooltip("Où le joueur se retrouve debout après « Jouer » (posé au sol).")]
        public Transform standPoint;
        [Tooltip("Ce que le joueur regarde en s'asseyant (l'écran de la télé).")]
        public Transform lookTarget;
        [Tooltip("Si la tête s'éloigne plus que ça (m) de la place assise, le joueur y est ramené.")]
        public float maxHeadDrift = 0.35f;

        [Header("Transitions")]
        public bool showOnStart = true;
        public float screenFadeDuration = 0.5f;
        public float blackFadeDuration = 0.35f;

        readonly List<Behaviour> m_Disabled = new List<Behaviour>();
        bool m_Open;
        bool m_Placed;
        Image m_Black;

        public bool IsOpen => m_Open;

        void Start()
        {
            if (origin == null)
                origin = FindFirstObjectByType<XROrigin>();

            if (!showOnStart)
            {
                screen.alpha = 0f;
                screen.gameObject.SetActive(false);
                return;
            }

            ShowPage(main: true);
            screen.alpha = 1f;
            m_Open = true;
            LockPlayer(true);
            StartCoroutine(SeatWhenTracked());
        }

        void Update()
        {
            if (!m_Open || !m_Placed || origin == null)
                return;

            // Le joueur a marché dans sa pièce (ou recentré le casque) : on le rassoit.
            var offset = origin.Camera.transform.position - seatEyes.position;
            offset.y = 0f;
            if (offset.magnitude > maxHeadDrift)
                Seat();
        }

        // Le casque ne donne la position de la tête qu'après quelques images : on attend avant d'asseoir le joueur.
        IEnumerator SeatWhenTracked()
        {
            var timeout = Time.time + 2f;
            while (Time.time < timeout && origin != null && origin.Camera.transform.localPosition.sqrMagnitude < 0.0001f)
                yield return null;
            yield return null;
            Seat();
            m_Placed = true;
        }

        void Seat()
        {
            if (origin == null || seatEyes == null)
                return;

            var forward = (lookTarget != null ? lookTarget.position : seatEyes.position + seatEyes.forward) - seatEyes.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                origin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
            // Place les yeux à hauteur "assis", que le joueur soit réellement assis ou debout.
            origin.MoveCameraToWorldLocation(seatEyes.position);
            Physics.SyncTransforms();
        }

        /// <summary>Bouton "Jouer".</summary>
        public void Play()
        {
            if (!m_Open)
                return;
            m_Open = false;
            StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            screen.interactable = false;
            yield return Fade(screen, 0f, screenFadeDuration);
            screen.gameObject.SetActive(false);

            yield return FadeBlack(1f);
            StandUp();
            LockPlayer(false);
            yield return FadeBlack(0f);
        }

        void StandUp()
        {
            if (origin == null || standPoint == null)
                return;

            var originTransform = origin.transform;
            var delta = standPoint.position - origin.Camera.transform.position;
            delta.y = 0f;
            var position = originTransform.position + delta;
            position.y = standPoint.position.y; // de retour au niveau du sol
            originTransform.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>Bouton "Commandes".</summary>
        public void ShowControls() => ShowPage(main: false);

        /// <summary>Bouton "Retour" de la page des commandes.</summary>
        public void ShowMain() => ShowPage(main: true);

        /// <summary>Bouton "Quitter".</summary>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void ShowPage(bool main)
        {
            if (mainPage != null)
                mainPage.SetActive(main);
            if (controlsPage != null)
                controlsPage.SetActive(!main);
        }

        // Désactive déplacement, rotation, téléportation et gravité. Les rayons des manettes restent actifs pour viser le menu.
        void LockPlayer(bool locked)
        {
            if (!locked)
            {
                foreach (var behaviour in m_Disabled)
                    if (behaviour != null)
                        behaviour.enabled = true;
                m_Disabled.Clear();
                return;
            }

            if (origin == null)
                return;

            foreach (var provider in origin.GetComponentsInChildren<LocomotionProvider>(true))
                Disable(provider);

            // Gestionnaire des manettes du XR Interaction Toolkit : sans lui, pousser le joystick
            // ne remplace plus le rayon (qui sert à viser le menu) par l'arc de téléportation.
            foreach (var behaviour in origin.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour.GetType().Name == "ControllerInputActionManager")
                    Disable(behaviour);
        }

        void Disable(Behaviour behaviour)
        {
            if (!behaviour.enabled)
                return;
            behaviour.enabled = false;
            m_Disabled.Add(behaviour);
        }

        static IEnumerator Fade(CanvasGroup group, float target, float duration)
        {
            var start = group.alpha;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                group.alpha = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }
            group.alpha = target;
        }

        // Fondu au noir devant les yeux, pour ne pas "téléporter" le joueur brutalement.
        IEnumerator FadeBlack(float target)
        {
            if (m_Black == null)
                m_Black = CreateBlackOverlay();
            if (m_Black == null)
                yield break;

            m_Black.enabled = true;
            var color = m_Black.color;
            var start = color.a;
            for (var t = 0f; t < blackFadeDuration; t += Time.deltaTime)
            {
                color.a = Mathf.Lerp(start, target, t / blackFadeDuration);
                m_Black.color = color;
                yield return null;
            }
            color.a = target;
            m_Black.color = color;
            m_Black.enabled = target > 0f;
        }

        Image CreateBlackOverlay()
        {
            var cam = origin != null ? origin.Camera : Camera.main;
            if (cam == null)
                return null;

            var go = new GameObject("Fondu au noir", typeof(Canvas), typeof(Image));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.02f;
            canvas.sortingOrder = short.MaxValue;

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }
    }
}
