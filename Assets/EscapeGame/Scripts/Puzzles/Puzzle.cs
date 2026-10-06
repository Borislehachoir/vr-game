using UnityEngine;
using UnityEngine.Events;

namespace EscapeGame
{
    /// <summary>
    /// Base de toutes les énigmes. Une énigme concrète appelle Solve() quand le joueur trouve la solution
    /// et Fail() sur une mauvaise réponse. Brancher les conséquences (ouvrir une porte, jouer un son...)
    /// sur les événements On Solved / On Failed dans l'Inspector.
    /// </summary>
    public abstract class Puzzle : MonoBehaviour
    {
        [Tooltip("Identifiant unique de l'énigme (utile pour la progression / sauvegarde).")]
        public string puzzleId;

        [Tooltip("Énigmes à résoudre avant que celle-ci puisse être validée. Laisser vide = aucune condition.")]
        public Puzzle[] requiredPuzzles = new Puzzle[0];

        [Space]
        public UnityEvent onSolved = new UnityEvent();
        public UnityEvent onFailed = new UnityEvent();

        public bool IsSolved { get; private set; }

        /// <summary>Nombre d'énigmes requises déjà résolues.</summary>
        public int SolvedRequiredCount
        {
            get
            {
                var count = 0;
                foreach (var puzzle in requiredPuzzles)
                    if (puzzle == null || puzzle.IsSolved)
                        count++;
                return count;
            }
        }

        public bool ArePrerequisitesMet => SolvedRequiredCount == requiredPuzzles.Length;

        protected void Solve()
        {
            if (IsSolved)
                return;
            IsSolved = true;
            OnSolved();
            onSolved.Invoke();
        }

        protected void Fail()
        {
            OnFailed();
            onFailed.Invoke();
        }

        public virtual void ResetPuzzle()
        {
            IsSolved = false;
        }

        protected virtual void OnSolved() { }
        protected virtual void OnFailed() { }

        protected virtual void Reset()
        {
            puzzleId = gameObject.name;
        }
    }
}
