using UnityEngine;
using UnityEngine.UI;

namespace EscapeGame
{
    /// <summary>
    /// Bouton du cadenas à molettes (flèche haut / bas d'une colonne, ou Valider).
    /// Se branche tout seul sur le DialCodePuzzle parent.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class DialCodeButton : MonoBehaviour
    {
        public enum ButtonAction { Up, Down, Submit }

        public ButtonAction action = ButtonAction.Up;
        [Tooltip("Colonne du chiffre (0 = gauche). Ignoré pour Submit.")]
        public int digitIndex;

        void Awake()
        {
            var puzzle = GetComponentInParent<DialCodePuzzle>();
            if (puzzle == null)
            {
                Debug.LogWarning($"{name} : aucun DialCodePuzzle trouvé dans les parents.", this);
                return;
            }

            GetComponent<Button>().onClick.AddListener(() =>
            {
                switch (action)
                {
                    case ButtonAction.Up: puzzle.Increment(digitIndex); break;
                    case ButtonAction.Down: puzzle.Decrement(digitIndex); break;
                    case ButtonAction.Submit: puzzle.Submit(); break;
                }
            });
        }
    }
}
