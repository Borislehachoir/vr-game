using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame
{
    /// <summary>
    /// Menu pause et écran de fin, ajoutés automatiquement dans les scènes de jeu (pas dans la scène du menu principal).
    /// Bouton menu de la manette gauche (les 3 traits) ou Échap au clavier : ouvre / ferme la pause.
    /// Même écran que le menu principal (prefab « Menu principal »), affiché devant le joueur :
    /// Reprendre / Commandes / Menu principal / Quitter, et le temps de jeu dans un coin.
    /// Le chrono démarre au chargement de la scène et s'arrête quand l'énigme finale (Ends Game) est résolue :
    /// l'écran de fin affiche alors le temps et un bouton « Retour au menu ».
    /// Pendant la pause, le jeu, le chrono, les sons et les vidéos sont figés.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [Tooltip("Distance (m) entre les yeux et le menu.")]
        public float distance = 1.1f;
        [Tooltip("Largeur (m) du menu.")]
        public float width = 0.9f;
        [Tooltip("Attente (s) entre la résolution de l'énigme finale et l'écran de fin.")]
        public float endDelay = 2f;

        const string k_SettingsResource = "Menu pause";

        PauseMenuSettings m_Settings;
        GameObject m_Menu;
        RectTransform m_Screen;
        GameObject m_MainPage;
        GameObject m_ControlsPage;
        Button m_ResumeButton, m_ControlsButton, m_MenuButton, m_QuitButton;
        TMP_Text m_TimerText;
        TMP_Text m_EndText;
        bool m_Paused;
        bool m_Finished;
        bool m_WasPressed;
        float m_Elapsed;
        float m_TimeScale = 1f;
        readonly List<Behaviour> m_Frozen = new List<Behaviour>();
        readonly List<VideoPlayer> m_PausedVideos = new List<VideoPlayer>();

        public bool IsPaused => m_Paused;
        /// <summary>Temps de jeu (s), hors pauses.</summary>
        public float Elapsed => m_Elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded += (scene, _) => AddTo(scene);
            AddTo(SceneManager.GetActiveScene());
        }

        static void AddTo(Scene scene)
        {
            if (FindAnyObjectByType<PauseMenu>() != null || FindAnyObjectByType<XROrigin>() == null)
                return;
            // Pas de pause dans la scène du menu principal.
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<MainMenu>(true) != null)
                    return;
            new GameObject("Menu pause").AddComponent<PauseMenu>();
        }

        void Start()
        {
            m_Settings = Resources.Load<PauseMenuSettings>(k_SettingsResource);

            // Énigme finale : le chrono s'arrête quand elle est résolue.
            foreach (var puzzle in FindObjectsByType<DialCodePuzzle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (puzzle.endsGame)
                    puzzle.onSolved.AddListener(OnFinalPuzzleSolved);
        }

        void Update()
        {
            if (!m_Finished)
                m_Elapsed += Time.deltaTime; // figé pendant la pause (temps arrêté)

            var pressed = IsMenuButtonPressed();
            if (pressed && !m_WasPressed && !m_Finished)
            {
                if (m_Paused)
                    Resume();
                else
                    Pause();
            }
            m_WasPressed = pressed;
        }

        // Bouton menu (3 traits) de la manette gauche, ou Échap au clavier pour tester dans l'éditeur.
        static bool IsMenuButtonPressed()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out var value) && value)
                return true;
            return Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
        }

        // ---------- Pause ----------

        public void Pause()
        {
            if (m_Paused || !BuildMenu())
                return;
            ShowMenu(end: false);
            Freeze();
        }

        public void Resume()
        {
            if (!m_Paused || m_Finished)
                return;
            m_Menu.SetActive(false);
            Unfreeze();
        }

        void Freeze()
        {
            m_Paused = true;
            m_TimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;

            m_PausedVideos.Clear();
            foreach (var video in FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None))
                if (video.isPlaying)
                {
                    video.Pause();
                    m_PausedVideos.Add(video);
                }

            // Ni déplacement, ni objets attrapables : seuls les rayons pour viser le menu restent.
            m_Frozen.Clear();
            foreach (var provider in FindObjectsByType<LocomotionProvider>(FindObjectsSortMode.None))
                Disable(provider);
            foreach (var interactable in FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None))
                Disable(interactable);
        }

        void Unfreeze()
        {
            m_Paused = false;
            Time.timeScale = m_TimeScale;
            AudioListener.pause = false;
            foreach (var video in m_PausedVideos)
                if (video != null)
                    video.Play();
            m_PausedVideos.Clear();

            foreach (var behaviour in m_Frozen)
                if (behaviour != null)
                    behaviour.enabled = true;
            m_Frozen.Clear();
        }

        void Disable(Behaviour behaviour)
        {
            if (!behaviour.enabled)
                return;
            behaviour.enabled = false;
            m_Frozen.Add(behaviour);
        }

        void OnDestroy()
        {
            // Changement de scène pendant la pause : le temps et le son ne doivent pas rester figés.
            if (m_Paused)
            {
                Time.timeScale = m_TimeScale;
                AudioListener.pause = false;
            }
        }

        // ---------- Fin du jeu ----------

        void OnFinalPuzzleSolved()
        {
            if (m_Finished)
                return;
            m_Finished = true; // chrono arrêté
            StartCoroutine(ShowEndScreen());
        }

        IEnumerator ShowEndScreen()
        {
            yield return new WaitForSecondsRealtime(endDelay); // laisse voir « Déverrouillé ! »
            if (!BuildMenu())
                yield break;
            if (m_Paused)
                Unfreeze();
            ShowMenu(end: true);
            Freeze();
        }

        void BackToMenu()
        {
            Unfreeze();
            var scene = m_Settings != null && !string.IsNullOrEmpty(m_Settings.menuScene) ? m_Settings.menuScene : "Menu";
            SceneManager.LoadScene(scene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Écran ----------

        void ShowMenu(bool end)
        {
            PlaceInFrontOfPlayer();
            ShowPage(main: true);

            m_ResumeButton.gameObject.SetActive(!end);
            m_ControlsButton.gameObject.SetActive(!end);
            m_QuitButton.gameObject.SetActive(!end);
            SetLabel(m_MenuButton, end ? m_Settings.backToMenuLabel : m_Settings.menuLabel);
            // Écran de fin : message au-dessus, un seul bouton en dessous.
            Place(m_MenuButton, end ? -120f : -45f);

            m_EndText.gameObject.SetActive(end);
            m_EndText.text = string.Format(m_Settings.endMessage, FormatTime(m_Elapsed));
            m_TimerText.gameObject.SetActive(!end);
            m_TimerText.text = string.Format(m_Settings.timerLabel, FormatTime(m_Elapsed));

            m_Menu.SetActive(true);
        }

        static string FormatTime(float seconds)
        {
            var total = Mathf.FloorToInt(seconds);
            var h = total / 3600;
            var m = total / 60 % 60;
            var s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }

        // Copie l'écran du menu principal, sans son script (qui lancerait la séquence de démarrage).
        bool BuildMenu()
        {
            if (m_Menu != null)
                return true;

            if (m_Settings == null)
                m_Settings = Resources.Load<PauseMenuSettings>(k_SettingsResource);
            if (m_Settings == null || m_Settings.menuPrefab == null)
            {
                Debug.LogWarning("Menu pause : fichier « Menu pause » (EscapeGame/Resources) ou son prefab introuvable.", this);
                return false;
            }

            m_Menu = Instantiate(m_Settings.menuPrefab, transform);
            m_Menu.name = "Écran pause";
            var source = m_Menu.GetComponent<MainMenu>();
            if (source == null)
            {
                Debug.LogWarning("Menu pause : le prefab n'a pas de composant MainMenu.", this);
                Destroy(m_Menu);
                return false;
            }
            source.enabled = false; // pas de Start : pas de séquence de démarrage
            Destroy(source);

            m_Screen = source.screen;
            m_MainPage = source.mainPage;
            m_ControlsPage = source.controlsPage;
            if (source.staticImage != null)
                source.staticImage.enabled = false;
            var group = source.menuGroup;
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            // Boutons : Reprendre / Commandes / Menu principal (copie de Quitter) / Quitter.
            m_ResumeButton = source.playButton;
            m_ControlsButton = source.controlsButton;
            m_QuitButton = source.quitButton;
            m_MenuButton = Instantiate(m_QuitButton.gameObject, m_QuitButton.transform.parent).GetComponent<Button>();
            m_MenuButton.name = "Menu principal";
            m_MenuButton.transform.SetSiblingIndex(m_QuitButton.transform.GetSiblingIndex());

            SetLabel(m_ResumeButton, m_Settings.resumeLabel);
            Listen(m_ResumeButton, Resume);
            Listen(m_ControlsButton, () => ShowPage(main: false));
            Listen(source.backButton, () => ShowPage(main: true));
            Listen(m_MenuButton, BackToMenu);
            Listen(m_QuitButton, Quit);

            // Quatre boutons au lieu de trois : un peu plus serrés.
            Place(m_ResumeButton, 165f);
            Place(m_ControlsButton, 60f);
            Place(m_MenuButton, -45f);
            Place(m_QuitButton, -150f);

            var subtitle = FindDeep(m_Menu.transform, "Sous-titre");
            TMP_Text subtitleText = null;
            if (subtitle != null && subtitle.TryGetComponent(out subtitleText))
                subtitleText.text = m_Settings.subtitle;
            var list = FindDeep(m_Menu.transform, "Liste");
            if (list != null && list.TryGetComponent<TMP_Text>(out var listText) && !string.IsNullOrEmpty(m_Settings.extraControls))
                listText.text += m_Settings.extraControls;

            // Chrono dans le coin en haut à droite, message de fin à la place des boutons :
            // copies du sous-titre, pour garder la même police.
            var textModel = subtitleText != null ? subtitleText : m_Menu.GetComponentInChildren<TMP_Text>(true);
            m_TimerText = CreateText(textModel, group.transform, "Chrono", new Vector2(1f, 1f), new Vector2(-40f, -40f),
                new Vector2(500f, 70f), new Vector2(1f, 1f), 40f, TextAlignmentOptions.TopRight);
            m_EndText = CreateText(textModel, m_MainPage.transform, "Message de fin", new Vector2(0.5f, 0.5f), new Vector2(400f, 90f),
                new Vector2(640f, 300f), new Vector2(0.5f, 0.5f), 54f, TextAlignmentOptions.Center);

            EnsureXREventSystem();
            m_Menu.SetActive(false);
            return true;
        }

        static TMP_Text CreateText(TMP_Text model, Transform parent, string name, Vector2 anchor, Vector2 position,
            Vector2 size, Vector2 pivot, float fontSize, TextAlignmentOptions alignment)
        {
            var text = Instantiate(model, parent);
            text.name = name;
            foreach (Transform child in text.transform)
                Destroy(child.gameObject);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        void PlaceInFrontOfPlayer()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            var cam = origin != null ? origin.Camera : Camera.main;
            if (cam == null || m_Screen == null)
                return;

            var head = cam.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();

            // Le Canvas se lit en regardant le long de son axe Z.
            m_Screen.SetPositionAndRotation(head.position + forward * distance - Vector3.up * 0.05f, Quaternion.LookRotation(forward, Vector3.up));
            var parentScale = m_Screen.parent != null ? m_Screen.parent.lossyScale.x : 1f;
            var scale = width / Mathf.Max(1f, m_Screen.sizeDelta.x) / Mathf.Max(1e-6f, parentScale);
            m_Screen.localScale = new Vector3(scale, scale, scale);
        }

        void ShowPage(bool main)
        {
            if (m_MainPage != null)
                m_MainPage.SetActive(main);
            if (m_ControlsPage != null)
                m_ControlsPage.SetActive(!main);
        }

        static void Place(Button button, float y)
        {
            if (button == null)
                return;
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 90f);
        }

        static void Listen(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        static void SetLabel(Button button, string label)
        {
            if (button == null || string.IsNullOrEmpty(label))
                return;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
                text.text = label;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        // Les rayons des manettes ne cliquent sur l'UI que si l'EventSystem utilise XRUIInputModule.
        static void EnsureXREventSystem()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                return;
            }
            if (eventSystem.GetComponent<XRUIInputModule>() != null)
                return;
            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                Destroy(module);
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
    }
}
