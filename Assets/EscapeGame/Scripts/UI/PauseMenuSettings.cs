using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Réglages du menu pause et de l'écran de fin (fichier « Menu pause » dans EscapeGame/Resources).
    /// </summary>
    [CreateAssetMenu(menuName = "Escape Game/Menu pause", fileName = "Menu pause")]
    public class PauseMenuSettings : ScriptableObject
    {
        [Tooltip("Prefab du menu principal, dont l'écran est réutilisé pour la pause.")]
        public GameObject menuPrefab;
        [Tooltip("Scène chargée par « Menu principal » / « Retour au menu ».")]
        public string menuScene = "Menu";

        [Header("Pause")]
        [Tooltip("Texte du bouton « Jouer » dans la pause.")]
        public string resumeLabel = "REPRENDRE";
        public string menuLabel = "MENU PRINCIPAL";
        [Tooltip("Texte affiché sous le logo.")]
        public string subtitle = "PAUSE";
        [Tooltip("Chrono affiché dans le coin. {0} = temps.")]
        public string timerLabel = "Temps : {0}";
        [Tooltip("Ajouté à la liste des commandes.")]
        [TextArea] public string extraControls = "\n\n<color=#D6A05C>Bouton menu</color> (manette gauche)\npause";

        [Header("Fin du jeu")]
        [Tooltip("{0} = temps final.")]
        [TextArea] public string endMessage = "Bravo !\nTu as fini l'escape game en <color=#D6A05C>{0}</color>";
        public string backToMenuLabel = "RETOUR AU MENU";
    }
}
