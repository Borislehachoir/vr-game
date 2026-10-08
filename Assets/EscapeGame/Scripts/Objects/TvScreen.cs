using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace EscapeGame
{
    /// <summary>
    /// Écran de télé qu'on allume et éteint (avec une TvRemote, ou depuis un événement).
    /// Allumée : écran blanc, ou la vidéo de "Video" si elle est renseignée. Éteinte : dalle noire brillante.
    /// Se crée avec le menu "Escape Game > Écran de télé".
    /// </summary>
    public class TvScreen : MonoBehaviour
    {
        public Renderer screen;
        [Tooltip("Matériau de l'écran éteint.")]
        public Material offMaterial;
        [Tooltip("Matériau de l'écran allumé (non éclairé, pour qu'il brille même dans le noir).")]
        public Material onMaterial;

        [Header("Vidéo")]
        [Tooltip("Vidéo jouée quand la télé est allumée. Vide = écran blanc.")]
        public VideoClip video;
        public bool loopVideo = true;
        [Tooltip("Résolution de l'image vidéo (largeur), la hauteur suit le format de la vidéo.")]
        public int videoWidth = 1280;

        [Header("Ambiance")]
        [Tooltip("Lumière allumée avec l'écran (lueur sur la pièce). Facultatif.")]
        public Light glow;
        public bool startOn;

        public UnityEvent onTurnedOn = new UnityEvent();
        public UnityEvent onTurnedOff = new UnityEvent();

        public bool IsOn { get; private set; }

        static readonly int k_BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int k_MainTex = Shader.PropertyToID("_MainTex");

        VideoPlayer m_Player;
        RenderTexture m_VideoTexture;
        Material m_VideoMaterial;

        void Awake() => Apply(startOn, false);

        void OnDestroy()
        {
            if (m_VideoTexture != null)
                m_VideoTexture.Release();
            Destroy(m_VideoTexture);
            Destroy(m_VideoMaterial);
        }

        public void Toggle() => Apply(!IsOn, true);
        public void TurnOn() => Apply(true, true);
        public void TurnOff() => Apply(false, true);

        void Apply(bool on, bool notify)
        {
            var changed = on != IsOn;
            IsOn = on;

            if (on && video != null)
                PlayVideo();
            else
            {
                if (m_Player != null)
                    m_Player.Stop();
                if (screen != null)
                    screen.sharedMaterial = on ? onMaterial : offMaterial;
            }

            if (glow != null)
                glow.enabled = on;

            if (notify && changed)
                (on ? onTurnedOn : onTurnedOff).Invoke();
        }

        void PlayVideo()
        {
            if (m_Player == null)
            {
                m_Player = gameObject.AddComponent<VideoPlayer>();
                m_Player.playOnAwake = false;
                m_Player.renderMode = VideoRenderMode.RenderTexture;
                m_Player.audioOutputMode = VideoAudioOutputMode.Direct;
            }

            if (m_VideoTexture == null)
            {
                var ratio = video.width > 0 ? (float)video.height / video.width : 9f / 16f;
                m_VideoTexture = new RenderTexture(videoWidth, Mathf.Max(1, Mathf.RoundToInt(videoWidth * ratio)), 0) { name = name + " (vidéo)" };
                m_VideoMaterial = new Material(onMaterial) { name = onMaterial.name + " (vidéo)" };
                m_VideoMaterial.SetTexture(k_BaseMap, m_VideoTexture);
                m_VideoMaterial.SetTexture(k_MainTex, m_VideoTexture);
            }

            m_Player.clip = video;
            m_Player.isLooping = loopVideo;
            m_Player.targetTexture = m_VideoTexture;
            if (screen != null)
                screen.sharedMaterial = m_VideoMaterial;
            m_Player.Play();
        }
    }
}
