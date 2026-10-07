using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame
{
    /// <summary>
    /// Feuille de papier (A4 par défaut) que l'on peut attraper et lire.
    /// Glisser un PNG dans "Front Texture" pour afficher un document au recto (et "Back Texture" pour le verso).
    /// Quand on l'attrape, la feuille vient dans la main, tenue par le bas, recto tourné vers le joueur.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PaperSheet : MonoBehaviour
    {
        [Header("Document")]
        [Tooltip("Image du recto (PNG). Idéalement au format A4 portrait, par exemple 2480 x 3508 px.")]
        public Texture2D frontTexture;
        [Tooltip("Image du verso (facultatif). Vide = verso blanc.")]
        public Texture2D backTexture;
        public Renderer frontRenderer;
        public Renderer backRenderer;

        [Header("Prise en main")]
        [Tooltip("Inclinaison (°) du haut de la feuille vers l'avant quand on la tient, pour mieux la lire.")]
        [Range(-60f, 60f)] public float holdTilt = 30f;
        [Tooltip("Hauteur (m) de la prise au-dessus du bas de la feuille.")]
        public float gripFromBottom = 0.03f;

        static readonly int k_BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int k_MainTex = Shader.PropertyToID("_MainTex");

        void Awake()
        {
            ApplyTextures();
            SetupGrip();
        }

        void OnValidate() => ApplyTextures();

        /// <summary>Change le document affiché (ex. depuis un événement d'énigme).</summary>
        public void SetDocument(Texture2D front, Texture2D back = null)
        {
            frontTexture = front;
            backTexture = back;
            ApplyTextures();
        }

        void ApplyTextures()
        {
            ApplyTexture(frontRenderer, frontTexture);
            ApplyTexture(backRenderer, backTexture);
        }

        // Le matériau est partagé entre toutes les feuilles : chaque feuille ne change que sa propre texture.
        static void ApplyTexture(Renderer target, Texture texture)
        {
            if (target == null)
                return;
            if (texture == null)
            {
                target.SetPropertyBlock(null);
                return;
            }

            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetTexture(k_BaseMap, texture);
            block.SetTexture(k_MainTex, texture);
            target.SetPropertyBlock(block);
        }

        void SetupGrip()
        {
            var grab = GetComponent<XRGrabInteractable>();
            grab.farAttachMode = InteractableFarAttachMode.Near;
            grab.useDynamicAttach = false;

            var attach = grab.attachTransform;
            if (attach == null || attach == transform)
            {
                attach = new GameObject("Prise en main").transform;
                attach.SetParent(transform, false);
                grab.attachTransform = attach;
            }

            // Tenue par le bas, au centre. La manette s'aligne sur "attach" : le tourner de -holdTilt
            // penche le haut de la feuille de +holdTilt vers l'avant.
            var height = frontRenderer != null ? frontRenderer.transform.localScale.y : 0.297f;
            attach.localPosition = new Vector3(0f, -height / 2f + gripFromBottom, 0f);
            attach.localRotation = Quaternion.Euler(-holdTilt, 0f, 0f);
        }
    }
}
