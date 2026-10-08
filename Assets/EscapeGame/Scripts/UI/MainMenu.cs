using System.Collections;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame
{
    /// <summary>
    /// Menu de début de partie, joué dans la pièce du jeu :
    /// le joueur apparaît assis sur le canapé face à la télé et ne peut que tourner la tête ;
    /// la télé grésille, puis affiche le logo et le bouton "Jouer". Après "Jouer", la télé s'éteint,
    /// le joueur se lève devant le canapé et peut se déplacer.
    /// La télé, le canapé et le joueur sont trouvés automatiquement : le prefab "Menu principal"
    /// fonctionne dans n'importe quelle scène qui les contient.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Header("Écran (dans le prefab)")]
        public RectTransform screen;
        public CanvasGroup menuGroup;
        public RawImage staticImage;
        public Button playButton;

        [Header("Pièce (vide = trouvé automatiquement)")]
        [Tooltip("La télé. Vide : celle de l'écran TvScreen de la scène, sinon l'objet nommé « TV ».")]
        public GameObject tv;
        [Tooltip("Le canapé. Vide : l'objet nommé « sofa » ou « Canapé ».")]
        public GameObject sofa;
        public XROrigin origin;
        [Tooltip("Position des yeux du joueur assis. Vide : calculée à partir du canapé.")]
        public Transform seatEyes;
        [Tooltip("Où le joueur se lève après « Jouer ». Vide : devant le canapé, côté télé.")]
        public Transform standPoint;

        [Header("Réglages")]
        [Tooltip("Hauteur des yeux d'une personne assise (m).")]
        public float seatEyeHeight = 1.15f;
        [Tooltip("Décalage de l'assise vers l'avant du canapé (m).")]
        public float seatForward = 0.1f;
        [Tooltip("Distance devant le canapé où le joueur se lève (m).")]
        public float standDistance = 0.5f;
        [Tooltip("Si la tête s'éloigne plus que ça (m) de la place assise, le joueur y est rassis.")]
        public float maxHeadDrift = 0.35f;

        [Header("Séquence")]
        [Tooltip("Télé éteinte au début (s).")]
        public float blackDuration = 1f;
        [Tooltip("Grésillement avant le menu (s).")]
        public float staticDuration = 2.5f;
        public float menuFadeDuration = 0.8f;
        [Range(0f, 1f)] public float staticVolume = 0.25f;

        // Écran du modèle "Modern_TV" (mêmes mesures que le menu "Écran de télé"), si la télé n'a pas de TvScreen.
        const float k_TvWidth = 1.604f;
        const float k_TvScreenWidth = 1.116f, k_TvScreenHeight = 0.627f, k_TvScreenCenterHeight = 0.4765f;
        const float k_TvFrontDepth = -0.025f;
        const float k_ScreenGap = 0.004f; // menu posé 4 mm devant la dalle
        static readonly string[] k_TvNames = { "TV", "Télé", "Television" };
        static readonly string[] k_SofaNames = { "sofa", "Canapé", "Canape", "Couch" };

        Vector3 m_SeatEyes, m_StandPoint, m_LookAt;
        bool m_Locked;
        bool m_Seated;
        TvScreen m_TvScreen;
        Texture2D m_Noise;
        Color32[] m_NoisePixels;
        float m_NextNoise;
        AudioSource m_StaticSound;
        Image m_Black;

        void Start()
        {
            if (origin == null)
                origin = FindAnyObjectByType<XROrigin>();
            FindRoom();
            if (!PlaceScreen())
            {
                Debug.LogWarning("Menu principal : télé introuvable (objet « TV »), menu désactivé.", this);
                gameObject.SetActive(false);
                return;
            }
            ComputeSeat();
            EnsureXREventSystem();

            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
            staticImage.enabled = false;
            playButton.onClick.AddListener(Play);

            LockPlayer(true);
            StartCoroutine(Intro());
        }

        void Update()
        {
            if (staticImage.enabled && Time.time >= m_NextNoise)
            {
                RefreshNoise();
                m_NextNoise = Time.time + 0.04f;
            }

            // Le joueur a marché dans sa vraie pièce (ou recentré le casque) : on le rassoit.
            if (m_Locked && m_Seated && origin != null)
            {
                var offset = origin.Camera.transform.position - m_SeatEyes;
                offset.y = 0f;
                if (offset.magnitude > maxHeadDrift)
                    Seat();
            }
        }

        // ---------- Séquence ----------

        IEnumerator Intro()
        {
            // Le casque ne donne la position de la tête qu'après quelques images.
            var timeout = Time.time + 2f;
            while (Time.time < timeout && origin != null && origin.Camera.transform.localPosition.sqrMagnitude < 0.0001f)
                yield return null;
            yield return null;
            Seat();
            m_Seated = true;

            yield return new WaitForSeconds(blackDuration);

            // La télé s'allume sur de la neige qui grésille.
            if (m_TvScreen != null && m_TvScreen.glow != null)
                m_TvScreen.glow.enabled = true;
            staticImage.color = Color.white;
            staticImage.enabled = true;
            PlayStaticSound();
            yield return new WaitForSeconds(staticDuration);

            // L'image se stabilise : le menu apparaît par-dessus la neige, qui s'efface.
            for (var t = 0f; t < menuFadeDuration; t += Time.deltaTime)
            {
                var k = t / menuFadeDuration;
                menuGroup.alpha = k;
                staticImage.color = new Color(1f, 1f, 1f, 1f - k);
                if (m_StaticSound != null)
                    m_StaticSound.volume = staticVolume * (1f - k);
                yield return null;
            }
            menuGroup.alpha = 1f;
            staticImage.enabled = false;
            if (m_StaticSound != null)
                m_StaticSound.Stop();
            menuGroup.interactable = true;
            menuGroup.blocksRaycasts = true;
        }

        /// <summary>Bouton "Jouer" : la télé s'éteint, le joueur se lève et peut se déplacer.</summary>
        public void Play()
        {
            if (!m_Locked || !menuGroup.interactable)
                return;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
            StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            for (var t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                menuGroup.alpha = 1f - t / 0.4f;
                yield return null;
            }
            menuGroup.alpha = 0f;
            if (m_TvScreen != null)
                m_TvScreen.TurnOff();

            yield return FadeBlack(1f, 0.35f);
            StandUp();
            LockPlayer(false);
            yield return FadeBlack(0f, 0.35f);
            screen.gameObject.SetActive(false);
        }

        // ---------- Pièce ----------

        void FindRoom()
        {
            m_TvScreen = tv != null ? tv.GetComponentInChildren<TvScreen>() : FindAnyObjectByType<TvScreen>();
            if (tv == null)
                tv = FindByName(k_TvNames);
            if (sofa == null)
                sofa = FindByName(k_SofaNames);
        }

        static GameObject FindByName(string[] names)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (names.Any(n => string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)))
                    return t.gameObject;
            return null;
        }

        // Pose l'écran du menu sur la dalle de la télé, à sa taille réelle.
        bool PlaceScreen()
        {
            Vector3 center, front, up;
            Vector2 size;

            if (m_TvScreen != null && m_TvScreen.screen != null)
            {
                // Écran déjà posé par le menu "Écran de télé" : on se cale exactement dessus.
                var t = m_TvScreen.screen.transform;
                front = -t.forward;
                up = t.up;
                center = t.position;
                size = new Vector2(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
            }
            else
            {
                var filter = tv != null
                    ? tv.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null)
                        .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault()
                    : null;
                if (filter == null)
                    return false;

                // Axes du modèle : largeur = plus grande dimension, profondeur = plus petite, hauteur = l'autre.
                var b = filter.sharedMesh.bounds;
                var axes = new[] { 0, 1, 2 }.OrderBy(i => b.size[i]).ToArray();
                int depth = axes[0], height = axes[1], width = axes[2];
                var k = b.size[width] / k_TvWidth; // unités du mesh par mètre du modèle
                // Le pied est en bas (hauteur ≥ 0) et la télé dépasse plus vers l'arrière que vers l'avant.
                var heightSign = b.max[height] >= -b.min[height] ? 1f : -1f;
                var depthSign = b.max[depth] >= -b.min[depth] ? 1f : -1f;

                var localUp = Vector3.zero;
                localUp[height] = heightSign;
                var localFront = Vector3.zero;
                localFront[depth] = -depthSign;
                var localCenter = Vector3.zero;
                localCenter[width] = b.center[width];
                localCenter[height] = heightSign * k_TvScreenCenterHeight * k;
                localCenter[depth] = depthSign * k_TvFrontDepth * k;

                var tr = filter.transform;
                center = tr.TransformPoint(localCenter);
                front = tr.TransformDirection(localFront).normalized;
                up = tr.TransformDirection(localUp).normalized;
                var scale = Mathf.Abs(tr.lossyScale.x);
                size = new Vector2(k_TvScreenWidth * k * scale, k_TvScreenHeight * k * scale);
            }

            // Le Canvas se lit en regardant le long de son axe Z : son Z part donc vers l'intérieur de la télé.
            var canvasSize = screen.sizeDelta;
            screen.SetPositionAndRotation(center + front * k_ScreenGap, Quaternion.LookRotation(-front, up));
            screen.localScale = Vector3.one; // échelle remise à plat avant de la calculer dans le repère du parent
            var parentScale = screen.parent != null ? screen.parent.lossyScale : Vector3.one;
            screen.localScale = new Vector3(size.x / canvasSize.x / parentScale.x, size.y / canvasSize.y / parentScale.y, 1f);
            m_LookAt = center;
            return true;
        }

        void ComputeSeat()
        {
            if (seatEyes != null)
                m_SeatEyes = seatEyes.position;
            if (standPoint != null)
                m_StandPoint = standPoint.position;
            if (seatEyes != null && standPoint != null)
                return;

            var renderers = sofa != null ? sofa.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (renderers.Length == 0)
            {
                Debug.LogWarning("Menu principal : canapé introuvable (objet « sofa »). Renseigner Seat Eyes et Stand Point.", this);
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            var toTv = m_LookAt - bounds.center;
            toTv.y = 0f;
            toTv.Normalize();
            var halfDepth = Mathf.Abs(toTv.x) * bounds.extents.x + Mathf.Abs(toTv.z) * bounds.extents.z;
            var seat = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + toTv * seatForward;

            if (seatEyes == null)
                m_SeatEyes = seat + Vector3.up * seatEyeHeight;
            if (standPoint == null)
                m_StandPoint = seat + toTv * (halfDepth + standDistance);
        }

        // ---------- Joueur ----------

        void Seat()
        {
            if (origin == null)
                return;
            var forward = m_LookAt - m_SeatEyes;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                origin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
            // Yeux à hauteur "assis", que le joueur soit réellement assis ou debout.
            origin.MoveCameraToWorldLocation(m_SeatEyes);
            Physics.SyncTransforms();
        }

        void StandUp()
        {
            if (origin == null)
                return;
            var originTransform = origin.transform;
            var delta = m_StandPoint - origin.Camera.transform.position;
            delta.y = 0f;
            var position = originTransform.position + delta;
            position.y = m_StandPoint.y; // pieds au sol
            originTransform.position = position;
            Physics.SyncTransforms();
        }

        // Bloque déplacement, rotation, téléportation et gravité. Les rayons restent actifs pour viser le menu.
        void LockPlayer(bool locked)
        {
            m_Locked = locked;
            if (origin == null)
                return;
            foreach (var provider in origin.GetComponentsInChildren<LocomotionProvider>(true))
                provider.enabled = !locked;
            // Gestionnaire des manettes du XR Interaction Toolkit : sans lui, pousser le joystick
            // ne remplace plus le rayon (qui sert à viser le menu) par l'arc de téléportation.
            foreach (var behaviour in origin.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour.GetType().Name == "ControllerInputActionManager")
                    behaviour.enabled = !locked;
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

        // ---------- Neige et son de la télé ----------

        void RefreshNoise()
        {
            if (m_Noise == null)
            {
                m_Noise = new Texture2D(160, 90, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Neige télé" };
                m_NoisePixels = new Color32[m_Noise.width * m_Noise.height];
                staticImage.texture = m_Noise;
            }
            for (var i = 0; i < m_NoisePixels.Length; i++)
            {
                var v = (byte)Random.Range(20, 235);
                m_NoisePixels[i] = new Color32(v, v, v, 255);
            }
            // Bande horizontale plus sombre qui défile, comme une vieille télé mal réglée.
            var band = (int)(Time.time * 40f) % m_Noise.height;
            for (var y = band; y < Mathf.Min(band + 6, m_Noise.height); y++)
            for (var x = 0; x < m_Noise.width; x++)
            {
                var p = m_NoisePixels[y * m_Noise.width + x];
                m_NoisePixels[y * m_Noise.width + x] = new Color32((byte)(p.r / 3), (byte)(p.g / 3), (byte)(p.b / 3), 255);
            }
            m_Noise.SetPixels32(m_NoisePixels);
            m_Noise.Apply(false);
        }

        // Son de grésillement généré par le script (pas de fichier audio à fournir).
        void PlayStaticSound()
        {
            if (staticVolume <= 0f)
                return;
            const int rate = 22050;
            var data = new float[rate];
            var previous = 0f;
            for (var i = 0; i < data.Length; i++)
            {
                // Bruit blanc légèrement adouci, avec un crépitement irrégulier.
                previous = Mathf.Lerp(previous, Random.Range(-1f, 1f), 0.6f);
                data[i] = previous * (Random.value < 0.002f ? 1f : 0.45f);
            }
            var clip = AudioClip.Create("Grésillement télé", data.Length, 1, rate, false);
            clip.SetData(data, 0);

            m_StaticSound = screen.gameObject.AddComponent<AudioSource>();
            m_StaticSound.clip = clip;
            m_StaticSound.loop = true;
            m_StaticSound.volume = staticVolume;
            m_StaticSound.spatialBlend = 1f; // le son vient de la télé
            m_StaticSound.minDistance = 1f;
            m_StaticSound.maxDistance = 8f;
            m_StaticSound.Play();
        }

        // ---------- Fondu au noir ----------

        IEnumerator FadeBlack(float target, float duration)
        {
            if (m_Black == null)
                m_Black = CreateBlackOverlay();
            if (m_Black == null)
                yield break;

            m_Black.enabled = true;
            var color = m_Black.color;
            var start = color.a;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                color.a = Mathf.Lerp(start, target, t / duration);
                m_Black.color = color;
                yield return null;
            }
            color.a = target;
            m_Black.color = color;
            m_Black.enabled = target > 0f;
        }

        // Voile noir collé devant les yeux, pour ne pas déplacer le joueur brutalement.
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

        void OnDestroy()
        {
            if (m_Noise != null)
                Destroy(m_Noise);
        }
    }
}
