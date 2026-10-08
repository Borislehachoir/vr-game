using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace EscapeGame
{
    /// <summary>
    /// Menu principal (scène Menu) affiché sur l'écran de la télé. Le joueur est assis sur le canapé,
    /// face à la télé : il ne peut que tourner la tête (déplacements et rotations désactivés).
    /// "Jouer" éteint l'écran, fait un fondu au noir et charge la scène du jeu.
    /// Créé par le menu "Escape Game > Menu > Créer ou mettre à jour la scène Menu".
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
        [Tooltip("Ce que le joueur regarde en s'asseyant (l'écran de la télé).")]
        public Transform lookTarget;
        [Tooltip("Si la tête s'éloigne plus que ça (m) de la place assise, le joueur y est ramené.")]
        public float maxHeadDrift = 0.35f;

        [Header("Jeu")]
        [Tooltip("Scène chargée par « Jouer » (doit être dans File > Build Profiles > Scene List).")]
        public string gameScene = "SampleScene";

        [Header("Transitions")]
        public float screenFadeDuration = 0.5f;
        public float blackFadeDuration = 0.35f;

        bool m_Open;
        bool m_Placed;
        Image m_Black;

        public bool IsOpen => m_Open;

        void Start()
        {
            if (origin == null)
                origin = FindFirstObjectByType<XROrigin>();

            ShowPage(main: true);
            screen.alpha = 1f;
            m_Open = true;
            LockPlayer();
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
            SceneManager.LoadScene(gameScene);
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
        void LockPlayer()
        {
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

        static void Disable(Behaviour behaviour) => behaviour.enabled = false;

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
