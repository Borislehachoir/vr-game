using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace EscapeGame
{
    /// <summary>
    /// Colore en jaune le rayon des manettes quand il vise un objet que l'on peut attraper ou utiliser
    /// (objet interactif ou écran d'énigme). Appliqué automatiquement à chaque chargement de scène :
    /// rien à ajouter dans les scènes.
    /// </summary>
    public static class InteractorRayHighlight
    {
        public static readonly Color highlightColor = new Color(1f, 0.85f, 0.1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            SceneManager.sceneLoaded += (_, _) => Apply();
            Apply();
        }

        /// <summary>Applique la couleur à tous les rayons de la scène (à rappeler si des manettes sont créées plus tard).</summary>
        public static void Apply()
        {
            var visuals = Object.FindObjectsByType<CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var visual in visuals)
            {
                Recolor(visual.hoverHitProperties);  // vise un objet attrapable / utilisable
                Recolor(visual.selectHitProperties); // objet tenu ou en train d'être attrapé
                Recolor(visual.uiHitProperties);     // vise un écran d'énigme
            }
        }

        // Garde la transparence d'origine (le rayon s'estompe vers le bout) et remplace seulement la couleur.
        static void Recolor(LineProperties properties)
        {
            if (properties == null)
                return;

            var gradient = new Gradient();
            var alphas = properties.gradient != null ? properties.gradient.alphaKeys : new[] { new GradientAlphaKey(1f, 0f) };
            gradient.SetKeys(new[] { new GradientColorKey(highlightColor, 0f), new GradientColorKey(highlightColor, 1f) }, alphas);
            properties.gradient = gradient;
            properties.adjustGradient = true;
        }
    }
}
