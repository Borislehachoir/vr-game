using UnityEngine;
using UnityEngine.UI;

namespace EscapeGame
{
    /// <summary>
    /// Page « Commandes » des menus (menu principal et pause) : remplace la liste écrite par l'image des manettes
    /// (« Controls Image » du fichier « Menu pause »), en grand sur tout l'écran. Le logo est caché sur cette page.
    /// </summary>
    static class ControlsPageImage
    {
        // Mesures dans l'écran du menu (1600 x 900).
        static readonly Vector2 k_ImageCenter = new Vector2(0f, 45f);
        const float k_ImageWidth = 1240f;
        static readonly Vector2 k_BackButton = new Vector2(0f, -395f);
        static readonly Color k_Backdrop = new Color(0.93f, 0.9f, 0.85f, 1f); // fond clair : traits noirs des manettes lisibles

        /// <summary>Met l'image à la place de la liste. Sans effet si l'image n'est pas réglée.</summary>
        public static void Setup(GameObject controlsPage, PauseMenuSettings settings = null)
        {
            if (controlsPage == null)
                return;
            if (settings == null)
                settings = Resources.Load<PauseMenuSettings>("Menu pause");
            var image = settings != null ? settings.controlsImage : null;
            if (image == null || controlsPage.transform.Find("Image commandes") != null)
                return;

            var page = controlsPage.transform;
            foreach (var name in new[] { "Liste", "Titre" })
            {
                var text = page.Find(name);
                if (text != null)
                    text.gameObject.SetActive(false);
            }
            var back = page.Find("Retour") as RectTransform;
            if (back != null)
                back.anchoredPosition = k_BackButton;

            var size = new Vector2(k_ImageWidth, k_ImageWidth * image.height / Mathf.Max(1f, image.width));

            // Chacun est placé en premier enfant : l'image, puis le fond derrière elle.
            var picture = new GameObject("Image commandes", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Place(picture.rectTransform, page, size);
            picture.texture = image;
            picture.raycastTarget = false;

            var backdrop = new GameObject("Fond commandes", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Place(backdrop.rectTransform, page, size + new Vector2(40f, 40f));
            backdrop.color = k_Backdrop;
            backdrop.raycastTarget = false;
        }

        /// <summary>Logo visible sur la page principale, caché sur la page des commandes (l'image prend la place).</summary>
        public static void ShowLogo(GameObject controlsPage, bool visible)
        {
            if (controlsPage == null || controlsPage.transform.Find("Image commandes") == null)
                return;
            var logo = controlsPage.transform.parent != null ? controlsPage.transform.parent.Find("Logo") : null;
            if (logo != null)
                logo.gameObject.SetActive(visible);
        }

        static void Place(RectTransform rect, Transform parent, Vector2 size)
        {
            rect.SetParent(parent, false);
            rect.SetSiblingIndex(0); // derrière le bouton Retour
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = k_ImageCenter;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.gameObject.layer = parent.gameObject.layer;
        }
    }
}
