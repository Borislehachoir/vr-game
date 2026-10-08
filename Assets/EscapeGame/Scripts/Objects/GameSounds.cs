using Unity.XR.CoreUtils;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Banque des sons du jeu (fichier « Sons du jeu » dans EscapeGame/Resources) : tiroirs, armoire, clé, cadenas,
    /// digicode, énigmes, pas et ambiance. Les objets jouent eux-mêmes leurs sons ; pour changer un son,
    /// le remplacer dans ce fichier. Vide = pas de son.
    /// </summary>
    [CreateAssetMenu(menuName = "Escape Game/Sons du jeu", fileName = "Sons du jeu")]
    public class GameSounds : ScriptableObject
    {
        [Header("Tiroirs")]
        public AudioClip drawerOpen;
        public AudioClip drawerClose;
        [Tooltip("Tiroir fermé par le cadenas qu'on essaie d'ouvrir.")]
        public AudioClip drawerLocked;

        [Header("Portes d'armoire")]
        public AudioClip doorOpen;
        public AudioClip doorClose;
        public AudioClip doorLocked;

        [Header("Clé et cadenas")]
        public AudioClip keyGrab;
        [Tooltip("La clé tombe ou est posée sur une surface.")]
        public AudioClip keyDrop;
        public AudioClip keyInsert;
        public AudioClip padlockUnlock;

        [Header("Digicode")]
        public AudioClip dialArrow;
        [Tooltip("Son de la touche Valider, un par chiffre (0 à 9). Vide = Dial Submit.")]
        public AudioClip[] dialDigits = new AudioClip[10];
        public AudioClip dialSubmit;
        public AudioClip codeRight;
        public AudioClip codeWrong;

        [Header("Autres énigmes (puzzle, tableau)")]
        public AudioClip puzzleSolved;
        public AudioClip puzzleFailed;

        [Header("Pas du joueur")]
        public AudioClip[] footsteps = new AudioClip[0];
        [Range(0f, 1f)] public float footstepVolume = 0.35f;
        [Tooltip("Distance (m) parcourue entre deux pas.")]
        public float stepLength = 0.7f;

        [Header("Ambiance (en boucle, partout)")]
        public AudioClip ambience;
        [Range(0f, 1f)] public float ambienceVolume = 0.2f;

        [Header("Notes de piano inquiétantes (au hasard, autour du joueur)")]
        public AudioClip[] pianoNotes = new AudioClip[0];
        [Range(0f, 1f)] public float pianoVolume = 0.5f;
        [Tooltip("Temps (s) minimum et maximum entre deux notes.")]
        public Vector2 pianoDelay = new Vector2(15f, 40f);
        [Tooltip("Hauteur des notes (1 = normale, moins = plus grave et désaccordé).")]
        public Vector2 pianoPitch = new Vector2(0.7f, 1f);
        [Tooltip("Distance (m) au joueur d'où viennent les notes.")]
        public Vector2 pianoDistance = new Vector2(3f, 6f);

        [Header("Réglages")]
        [Range(0f, 1f)] public float effectsVolume = 0.8f;
        [Tooltip("Distance (m) jusqu'à laquelle un bruit d'objet est à plein volume.")]
        public float fullVolumeDistance = 1.5f;
        [Tooltip("Distance (m) au-delà de laquelle un bruit d'objet ne s'entend plus.")]
        public float maxDistance = 12f;

        const string k_ResourceName = "Sons du jeu";
        const int k_PoolSize = 12;

        static GameSounds s_Bank;
        static bool s_Loaded;
        static AudioSource[] s_Pool;
        static int s_Next;

        /// <summary>La banque de sons (null si le fichier est absent).</summary>
        public static GameSounds Bank
        {
            get
            {
                if (!s_Loaded)
                {
                    s_Bank = Resources.Load<GameSounds>(k_ResourceName);
                    s_Loaded = true;
                }
                return s_Bank;
            }
        }

        /// <summary>Joue un bruit à cet endroit (son 3D).</summary>
        public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
        {
            var bank = Bank;
            if (clip == null || bank == null)
                return;

            // Légère variation de hauteur : évite l'effet « copier-coller » des sons répétés.
            PlayAt(clip, position, volume * bank.effectsVolume, Random.Range(0.96f, 1.04f));
        }

        /// <summary>Joue un son 3D avec ce volume (sans le volume des effets) et cette hauteur.</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            var bank = Bank;
            if (clip == null || bank == null)
                return;

            var source = NextSource(bank);
            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = pitch;
            source.Play();
        }

        /// <summary>Joue un des sons au hasard.</summary>
        public static void PlayRandom(AudioClip[] clips, Vector3 position, float volume = 1f)
        {
            if (clips == null || clips.Length == 0)
                return;
            Play(clips[Random.Range(0, clips.Length)], position, volume);
        }

        static AudioSource NextSource(GameSounds bank)
        {
            if (s_Pool == null || s_Pool[0] == null)
            {
                var root = new GameObject("Sons");
                s_Pool = new AudioSource[k_PoolSize];
                for (var i = 0; i < k_PoolSize; i++)
                {
                    var source = new GameObject("Son " + (i + 1)).AddComponent<AudioSource>();
                    source.transform.SetParent(root.transform, false);
                    source.playOnAwake = false;
                    source.spatialBlend = 1f;
                    source.dopplerLevel = 0f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = bank.fullVolumeDistance;
                    source.maxDistance = bank.maxDistance;
                    s_Pool[i] = source;
                }
            }

            // Une source libre, sinon la plus ancienne.
            for (var i = 0; i < k_PoolSize; i++)
            {
                var source = s_Pool[(s_Next + i) % k_PoolSize];
                if (!source.isPlaying)
                {
                    s_Next = (s_Next + i + 1) % k_PoolSize;
                    return source;
                }
            }
            var oldest = s_Pool[s_Next];
            s_Next = (s_Next + 1) % k_PoolSize;
            return oldest;
        }

        // Ambiance et pas : ajoutés automatiquement dans chaque scène où se trouve le joueur.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            s_Pool = null;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => AddPlayerSounds();
            AddPlayerSounds();
        }

        static void AddPlayerSounds()
        {
            var bank = Bank;
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (bank == null || origin == null || origin.GetComponent<PlayerSounds>() != null)
                return;
            origin.gameObject.AddComponent<PlayerSounds>();
        }
    }
}
