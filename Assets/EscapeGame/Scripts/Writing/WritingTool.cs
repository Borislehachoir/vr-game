using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EscapeGame
{
    /// <summary>
    /// Outil d'écriture tenu en main : feutre (Ink) ou effaceur (Erase).
    /// Quand sa pointe touche une WritableSurface, il dessine (ou efface) à cet endroit.
    /// Pour changer de modèle 3D : remplacer l'enfant "Modèle" et replacer l'enfant "Pointe"
    /// au bout de l'outil, son axe bleu (Z) tourné vers l'extérieur.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class WritingTool : MonoBehaviour
    {
        public enum ToolMode { Ink, Erase }

        public ToolMode mode = ToolMode.Ink;
        [Tooltip("Point de contact. Son axe Z (bleu) doit pointer hors de l'outil.")]
        public Transform tip;

        [Header("Trait")]
        public Color color = new Color(0.1f, 0.25f, 0.85f, 1f);
        [Tooltip("Rayon du trait, en mètres.")]
        public float radius = 0.003f;
        [Tooltip("Douceur du bord du trait (petit = net, grand = flou).")]
        [Range(0.05f, 1f)] public float softness = 0.35f;

        [Header("Contact")]
        [Tooltip("Marge (m) entre la pointe et la surface visible pour qu'on considère qu'elle touche.")]
        public float contactTolerance = 0.002f;
        [Tooltip("Profondeur (m) jusqu'à laquelle on écrit encore si la pointe s'enfonce dans la surface.")]
        public float penetrationDepth = 0.1f;
        [Tooltip("N'écrit que si l'outil est tenu (évite d'écrire quand il est posé contre le tableau).")]
        public bool onlyWhenHeld = true;

        [Header("Prise en main")]
        [Tooltip("Utilise la prise conseillée pour ce type d'outil. Décocher pour régler les deux valeurs ci-dessous.")]
        public bool useRecommendedGrip = true;
        [Tooltip("Distance (m) entre la main et la pointe, le long de l'outil.")]
        public float gripDistance = 0.065f;
        [Tooltip("Inclinaison (°) de la pointe vers le bas par rapport à la manette.")]
        [Range(-90f, 90f)] public float gripTilt = 35f;

        [Header("Vibration")]
        [Range(0f, 1f)] public float hapticAmplitude = 0.1f;

        // Le rayon va un peu plus loin que la zone de contact pour toucher le collider (épais) de la surface.
        const float k_ColliderSearchMargin = 0.05f;

        readonly RaycastHit[] m_Hits = new RaycastHit[8];
        XRGrabInteractable m_Grab;
        WritableSurface m_LastSurface;
        Vector2 m_LastUV;
        bool m_Touching;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            if (tip == null)
                tip = transform;
            SetupGrip();

            // Remplace la prise par défaut du XR Grab Interactable : même comportement,
            // mais la pointe ne peut plus traverser les zones d'écriture.
            if (GetComponent<WritingToolGrabTransformer>() == null)
                gameObject.AddComponent<WritingToolGrabTransformer>();

            // Revient sur le tableau s'il reste au sol. Ajouter le composant à la main pour régler le délai
            // (ou le désactiver pour supprimer le retour).
            if (GetComponent<ReturnToStartOnFloor>() == null)
                gameObject.AddComponent<ReturnToStartOnFloor>();
        }

        /// <summary>
        /// L'objet vient toujours se placer dans la main (même attrapé de loin), dans une pose fixe :
        /// pointe à gripDistance de la main, inclinée de gripTilt vers le bas.
        /// </summary>
        void SetupGrip()
        {
            if (useRecommendedGrip)
            {
                gripDistance = mode == ToolMode.Ink ? 0.065f : 0.04f;
                gripTilt = mode == ToolMode.Ink ? 35f : 15f;
            }

            m_Grab.farAttachMode = InteractableFarAttachMode.Near;
            m_Grab.useDynamicAttach = false;

            var attach = m_Grab.attachTransform;
            if (attach == null || attach == transform)
            {
                attach = new GameObject("Prise en main").transform;
                attach.SetParent(transform, false);
                m_Grab.attachTransform = attach;
            }

            // La manette s'aligne sur "attach" : en le tournant de -gripTilt autour de l'axe X de la pointe,
            // l'outil se retrouve incliné de +gripTilt (pointe vers le bas) dans la main.
            attach.SetPositionAndRotation(
                tip.position - tip.forward * gripDistance,
                tip.rotation * Quaternion.Euler(-gripTilt, 0f, 0f));
        }

        void Update()
        {
            if ((onlyWhenHeld && !m_Grab.isSelected) || !TryFindSurface(out var surface, out var point))
            {
                m_Touching = false;
                return;
            }

            var uv = surface.WorldToUV(point);
            var newStroke = !m_Touching || surface != m_LastSurface;
            var from = newStroke ? uv : m_LastUV;
            surface.DrawLine(from, uv, color, radius, softness, mode == ToolMode.Erase, newStroke);

            m_LastSurface = surface;
            m_LastUV = uv;
            m_Touching = true;
            Vibrate();
        }

        bool TryFindSurface(out WritableSurface surface, out Vector3 point)
        {
            surface = null;
            point = default;

            // Rayon lancé depuis l'intérieur de l'outil vers la pointe : on détecte la surface
            // même si la pointe s'est un peu enfoncée dedans.
            // Le collider sert seulement à trouver la zone : le contact est mesuré sur la surface visible
            // (le plan du Quad), pas sur la face du collider qui dépasse devant.
            var ray = new Ray(tip.position - tip.forward * penetrationDepth, tip.forward);
            var maxDistance = penetrationDepth + contactTolerance;
            var count = Physics.RaycastNonAlloc(ray, m_Hits, maxDistance + k_ColliderSearchMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            var closest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var candidate = m_Hits[i].collider.GetComponentInParent<WritableSurface>();
                if (candidate == null)
                    continue;

                var plane = new Plane(candidate.transform.forward, candidate.transform.position);
                if (!plane.Raycast(ray, out var distance) || distance > maxDistance || distance >= closest)
                    continue;

                closest = distance;
                surface = candidate;
                point = ray.GetPoint(distance);
            }

            return surface != null;
        }

        void Vibrate()
        {
            if (hapticAmplitude <= 0f)
                return;
            foreach (var interactor in m_Grab.interactorsSelecting)
                if (interactor is XRBaseInputInteractor inputInteractor)
                    inputInteractor.SendHapticImpulse(hapticAmplitude, Time.deltaTime * 2f);
        }

        void OnDrawGizmosSelected()
        {
            var t = tip != null ? tip : transform;
            Gizmos.color = mode == ToolMode.Ink ? color : Color.white;
            Gizmos.DrawLine(t.position, t.position + t.forward * contactTolerance);
            Gizmos.DrawWireSphere(t.position, radius);
        }
    }
}
