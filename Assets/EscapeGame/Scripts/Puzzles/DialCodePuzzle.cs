using System.Collections;
using TMPro;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Cadenas à code : chiffres modifiés avec les flèches haut / bas, puis "Valider".
    /// Mode "un chiffre à la fois" (par défaut) : un seul chiffre est affiché. "Valider" enregistre ce chiffre et passe
    /// au suivant SANS dire s'il est juste ; le code complet n'est vérifié qu'après le dernier chiffre.
    /// Faux : on recommence au premier chiffre (impossible de trouver les chiffres un par un en essayant).
    /// Sinon, toutes les molettes sont affichées et validées ensemble.
    /// Any Order (par défaut) : les chiffres de Solution sont acceptés dans n'importe quel ordre.
    /// Si des énigmes requises (Required Puzzles) ne sont pas résolues, le bon code est refusé.
    /// </summary>
    public class DialCodePuzzle : Puzzle
    {
        [Header("Code")]
        [Tooltip("Code à trouver. En mode un chiffre à la fois, les chiffres sont demandés dans cet ordre.")]
        public string solution = "976";
        [Tooltip("Un seul chiffre affiché, saisi un par un ; le code n'est vérifié qu'à la fin.")]
        public bool oneDigitAtATime = true;
        [Tooltip("Les chiffres du code sont acceptés dans n'importe quel ordre (ex. 976, 679, 769...).")]
        public bool anyOrder = true;
        [Tooltip("Énigme finale : le bon code arrête le chrono et affiche l'écran de fin.")]
        public bool endsGame = true;

        [Header("Affichage")]
        [Tooltip("Un texte par chiffre, de gauche à droite. En mode un chiffre à la fois, seul le premier est utilisé.")]
        public TMP_Text[] digitTexts = new TMP_Text[0];
        public TMP_Text feedback;
        public string successMessage = "Déverrouillé !";
        public string errorMessage = "Code incorrect";
        [Tooltip("Mode un chiffre à la fois : chiffres déjà saisis. {0} = saisie (ex. « 9 7 _ »).")]
        public string progressMessage = "Code : {0}";
        public string lockedMessage = "Il reste des énigmes à résoudre ({0}/{1})";
        public Color successColor = new Color(0.3f, 0.9f, 0.4f);
        public Color errorColor = new Color(1f, 0.35f, 0.35f);
        public Color lockedColor = new Color(1f, 0.75f, 0.3f);

        int[] m_Digits;
        string m_Entered = "";

        void Awake()
        {
            m_Digits = new int[digitTexts.Length];
            if (oneDigitAtATime)
                ShowSingleDial();
        }

        void Start()
        {
            Refresh();
            ShowProgress();
        }

        // Garde la première colonne (centrée) et cache les autres.
        void ShowSingleDial()
        {
            for (var i = 0; i < digitTexts.Length; i++)
            {
                if (digitTexts[i] == null)
                    continue;
                var column = digitTexts[i].transform.parent as RectTransform;
                if (column == null || column == transform)
                    continue;
                if (i == 0)
                    column.anchoredPosition = new Vector2(0f, column.anchoredPosition.y);
                else
                    column.gameObject.SetActive(false);
            }
        }

        public void Increment(int index) => Change(index, 1);
        public void Decrement(int index) => Change(index, -1);

        void Change(int index, int delta)
        {
            if (oneDigitAtATime)
                index = 0;
            if (IsSolved || index < 0 || index >= m_Digits.Length)
                return;
            m_Digits[index] = (m_Digits[index] + delta + 10) % 10;
            GameSounds.Play(GameSounds.Bank?.dialArrow, transform.position);
            ShowProgress();
            Refresh();
        }

        public void Submit()
        {
            if (IsSolved || m_Digits.Length == 0)
                return;

            string code;
            if (oneDigitAtATime)
            {
                // Chiffre enregistré sans dire s'il est juste ; le code n'est vérifié qu'au dernier.
                m_Entered += m_Digits[0];
                var digit = m_Digits[0];
                m_Digits[0] = 0;
                Refresh();
                if (m_Entered.Length < solution.Length)
                {
                    PlayDigitSound(digit);
                    ShowProgress();
                    return;
                }
                code = m_Entered;
                m_Entered = "";
            }
            else
                code = CurrentCode();

            if (!Matches(code))
            {
                Fail();
                return;
            }
            if (!ArePrerequisitesMet)
            {
                SetFeedback(string.Format(lockedMessage, SolvedRequiredCount, requiredPuzzles.Length), lockedColor);
                return;
            }
            Solve();
        }

        // Bip de la touche Valider : un son par chiffre s'il existe, sinon le bip commun.
        void PlayDigitSound(int digit)
        {
            var bank = GameSounds.Bank;
            if (bank == null)
                return;
            var clip = bank.dialDigits != null && digit < bank.dialDigits.Length ? bank.dialDigits[digit] : null;
            GameSounds.Play(clip != null ? clip : bank.dialSubmit, transform.position);
        }

        protected override AudioClip SolvedSound => GameSounds.Bank?.codeRight;
        protected override AudioClip FailedSound => GameSounds.Bank?.codeWrong;

        // Même code, ou mêmes chiffres dans un autre ordre si Any Order est coché.
        bool Matches(string code)
        {
            if (!anyOrder)
                return code == solution;
            var a = code.ToCharArray();
            var b = solution.ToCharArray();
            System.Array.Sort(a);
            System.Array.Sort(b);
            return new string(a) == new string(b);
        }

        public override void ResetPuzzle()
        {
            base.ResetPuzzle();
            m_Entered = "";
            System.Array.Clear(m_Digits, 0, m_Digits.Length);
            foreach (var text in digitTexts)
                if (text != null)
                    text.color = Color.white;
            Refresh();
            ShowProgress();
        }

        protected override void OnSolved()
        {
            SetFeedback(successMessage, successColor);
            foreach (var text in digitTexts)
                if (text != null)
                    text.color = successColor;
        }

        protected override void OnFailed()
        {
            SetFeedback(errorMessage, errorColor);
            StopAllCoroutines();
            StartCoroutine(Shake());
        }

        // Mode un chiffre à la fois : chiffres déjà saisis, puis "_" pour les suivants (ex. « 9 7 _ »).
        void ShowProgress()
        {
            if (!oneDigitAtATime || IsSolved)
            {
                SetFeedback("", Color.white);
                return;
            }
            var slots = new string[solution.Length];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = i < m_Entered.Length ? m_Entered[i].ToString() : "_";
            SetFeedback(string.Format(progressMessage, string.Join(" ", slots)), Color.white);
        }

        string CurrentCode()
        {
            var code = "";
            foreach (var digit in m_Digits)
                code += digit;
            return code;
        }

        void Refresh()
        {
            for (var i = 0; i < digitTexts.Length; i++)
                if (digitTexts[i] != null)
                    digitTexts[i].text = m_Digits[i].ToString();
        }

        void SetFeedback(string message, Color color)
        {
            if (feedback == null)
                return;
            feedback.text = message;
            feedback.color = color;
        }

        IEnumerator Shake()
        {
            var targets = new RectTransform[digitTexts.Length];
            var origins = new Vector2[digitTexts.Length];
            for (var i = 0; i < digitTexts.Length; i++)
            {
                targets[i] = digitTexts[i].rectTransform;
                origins[i] = targets[i].anchoredPosition;
            }

            for (var t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                var offset = Vector2.right * Mathf.Sin(t * 60f) * 10f * (1f - t / 0.3f);
                for (var i = 0; i < targets.Length; i++)
                    targets[i].anchoredPosition = origins[i] + offset;
                yield return null;
            }

            for (var i = 0; i < targets.Length; i++)
                targets[i].anchoredPosition = origins[i];
        }
    }
}
