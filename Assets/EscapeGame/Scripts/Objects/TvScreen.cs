using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.Video;

namespace EscapeGame
{
    /// <summary>
    /// Écran de télé à chaînes (avec une TvRemote, ou depuis un événement).
    /// Chaque appui sur la télécommande passe à la vidéo suivante de "Videos" ; après la dernière, la télé s'éteint.
    /// Sans vidéo : écran blanc. Éteinte : dalle noire brillante.
    /// Se crée avec le menu "Escape Game > Écran de télé".
    /// </summary>
    public class TvScreen : MonoBehaviour
    {
        public Renderer screen;
        [Tooltip("Matériau de l'écran éteint.")]
        public Material offMaterial;
        [Tooltip("Matériau de l'écran allumé (non éclairé, pour qu'il brille même dans le noir).")]
        public Material onMaterial;

        [Header("Vidéos")]
        [Tooltip("Vidéos jouées dans l'ordre, une par appui sur la télécommande. Vide = écran blanc.")]
        public VideoClip[] videos = new VideoClip[0];
        [FormerlySerializedAs("video")]
        [SerializeField, HideInInspector] VideoClip m_LegacyVideo; // ancien champ "Video" (une seule vidéo)
        public bool loopVideo = true;
        [Tooltip("Résolution de l'image vidéo (largeur), la hauteur suit le format de la vidéo.")]
        public int videoWidth = 1280;
        public bool startOn;
        [Tooltip("Lueur de l'écran : jamais allumée par la télé (seulement par le menu principal), éteinte avec elle.")]
        public Light glow;

        [Header("Son")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Le son est à plein volume jusqu'à cet objet (ex. le canapé), puis baisse derrière. Vide = Full Volume Distance.")]
        public Transform fullVolumeUntil;
        [Tooltip("Distance (m) depuis la télé où le son est à plein volume, si Full Volume Until est vide.")]
        public float fullVolumeDistance = 3f;
        [Tooltip("Distance (m) sur laquelle le son s'éteint petit à petit après la zone à plein volume.")]
        public float fadeDistance = 2.5f;

        public UnityEvent onTurnedOn = new UnityEvent();
        public UnityEvent onTurnedOff = new UnityEvent();

        public bool IsOn => m_Channel >= 0;
        /// <summary>Vidéo en cours (index dans Videos), -1 si éteinte.</summary>
        public int Channel => m_Channel;

        static readonly int k_BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int k_MainTex = Shader.PropertyToID("_MainTex");

        VideoPlayer m_Player;
        RenderTexture m_VideoTexture;
        Material m_VideoMaterial;
        int m_Channel = -1;

        void OnValidate()
        {
            // Reprend la vidéo de l'ancien champ "Video" comme première vidéo.
            if (m_LegacyVideo != null && (videos == null || videos.Length == 0))
                videos = new[] { m_LegacyVideo };
            m_LegacyVideo = null;
        }

        void Awake()
        {
            OnValidate();
            SetChannel(startOn ? 0 : -1, false);
        }

        void OnDestroy()
        {
            if (m_VideoTexture != null)
                m_VideoTexture.Release();
            Destroy(m_VideoTexture);
            Destroy(m_VideoMaterial);
        }

        /// <summary>Éteinte → 1re vidéo → 2e vidéo → ... → éteinte.</summary>
        public void Toggle() => NextChannel();

        public void NextChannel()
        {
            var count = Mathf.Max(1, videos.Length);
            SetChannel(m_Channel + 1 < count ? m_Channel + 1 : -1, true);
        }

        public void TurnOn() => SetChannel(Mathf.Max(0, m_Channel), true);
        public void TurnOff() => SetChannel(-1, true);

        void SetChannel(int channel, bool notify)
        {
            var wasOn = IsOn;
            m_Channel = channel;

            var clip = IsOn && channel < videos.Length ? videos[channel] : null;
            if (clip != null)
                PlayVideo(clip);
            else
            {
                if (m_Player != null)
                    m_Player.Stop();
                if (screen != null)
                    screen.sharedMaterial = IsOn ? onMaterial : offMaterial;
            }

            if (!IsOn && glow != null)
                glow.enabled = false;

            if (notify && wasOn != IsOn)
                (IsOn ? onTurnedOn : onTurnedOff).Invoke();
        }

        void PlayVideo(VideoClip clip)
        {
            if (m_Player == null)
            {
                m_Player = gameObject.AddComponent<VideoPlayer>();
                m_Player.playOnAwake = false;
                m_Player.renderMode = VideoRenderMode.RenderTexture;
                m_Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                m_Player.controlledAudioTrackCount = 1;
                m_Player.EnableAudioTrack(0, true);
                m_Player.SetTargetAudioSource(0, CreateAudioSource());
            }

            var ratio = clip.width > 0 ? (float)clip.height / clip.width : 9f / 16f;
            var height = Mathf.Max(1, Mathf.RoundToInt(videoWidth * ratio));
            if (m_VideoTexture == null || m_VideoTexture.height != height)
            {
                if (m_VideoTexture != null)
                {
                    m_VideoTexture.Release();
                    Destroy(m_VideoTexture);
                }
                m_VideoTexture = new RenderTexture(videoWidth, height, 0) { name = name + " (vidéo)" };
                if (m_VideoMaterial == null)
                    m_VideoMaterial = new Material(onMaterial) { name = onMaterial.name + " (vidéo)" };
                m_VideoMaterial.SetTexture(k_BaseMap, m_VideoTexture);
                m_VideoMaterial.SetTexture(k_MainTex, m_VideoTexture);
            }

            m_Player.Stop();
            m_Player.clip = clip;
            m_Player.isLooping = loopVideo;
            m_Player.targetTexture = m_VideoTexture;
            if (screen != null)
                screen.sharedMaterial = m_VideoMaterial;
            m_Player.Play();
        }

        // Son 3D qui sort de l'écran : plein volume dans la zone devant la télé, puis baisse jusqu'au silence.
        AudioSource CreateAudioSource()
        {
            var full = fullVolumeDistance;
            if (fullVolumeUntil != null)
                full = DistanceToFarSide(fullVolumeUntil);
            full = Mathf.Max(0.1f, full);
            var max = full + Mathf.Max(0.1f, fadeDistance);

            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = full;
            source.maxDistance = max;
            // Courbe en distance relative (0 = télé, 1 = Max Distance) : plat jusqu'au bout de la zone, puis descente douce.
            var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(full / max, 1f), new Keyframe(1f, 0f));
            curve.SmoothTangents(1, 0f);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
            return source;
        }

        // Distance horizontale entre l'écran et le côté le plus éloigné de l'objet (ex. le dossier du canapé).
        float DistanceToFarSide(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return Flat(target.position - transform.position).magnitude;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            var toCenter = Flat(bounds.center - transform.position);
            var direction = toCenter.sqrMagnitude > 1e-6f ? toCenter.normalized : Vector3.forward;
            var reach = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;
            return toCenter.magnitude + reach;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Zone du son dans la vue Scene : vert = plein volume, orange = le son s'éteint.
        void OnDrawGizmosSelected()
        {
            var full = fullVolumeUntil != null ? DistanceToFarSide(fullVolumeUntil) : fullVolumeDistance;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, full);
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, full + fadeDistance);
        }
    }
}
