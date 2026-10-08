using System.Collections;
using TMPro;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Cadenas à code : chiffres modifiés avec les flèches haut / bas, puis "Valider".
    /// Mode "un chiffre à la fois" (par défaut) : un seul chiffre est affiché ; on le valide, et s'il est juste
    /// on passe au suivant (dans l'ordre du code). Sinon, tous les chiffres sont validés ensemble.
    /// Si des énigmes requises (Required Puzzles) ne sont pas résolues, le dernier chiffre est refusé.
    /// </summary>
    public class DialCodePuzzle : Puzzle
    {
        [Header("Code")]
        public string solution = "976";
        [Tooltip("Un seul chiffre affiché, validé un par un dans l'ordre du code.")]
        public bool oneDigitAtATime = true;

        [Header("Affichage")]
        [Tooltip("Un texte par chiffre, de gauche à droite. En mode un chiffre à la fois, seul le premier est utilisé.")]
        public TMP_Text[] digitTexts = new TMP_Text[0];
        public TMP_Text feedback;
        public string successMessage = "Déverrouillé !";
        public string errorMessage = "Code incorrect";
        [Tooltip("Mode un chiffre à la fois : message après un bon chiffre. {0} = chiffre suivant, {1} = nombre de chiffres.")]
        public string digitOkMessage = "Correct ! Chiffre {0} / {1}";
        [Tooltip("Mode un chiffre à la fois : rappel du chiffre en cours.")]
        public string stepMessage = "Chiffre {0} / {1}";
        public string lockedMessage = "Il reste des énigmes à résoudre ({0}/{1})";
        public Color successColor = new Color(0.3f, 0.9f, 0.4f);
        public Color errorColor = new Color(1f, 0.35f, 0.35f);
        public Color lockedColor = new Color(1f, 0.75f, 0.3f);

        int[] m_Digits;
        int m_Step;

        int DigitCount => oneDigitAtATime ? solution.Length : digitTexts.Length;

        void Awake()
        {
            m_Digits = new int[digitTexts.Length];
            if (oneDigitAtATime)
                ShowSingleDial();
        }

        void Start()
        {
            Refresh();
            ShowStep();
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
            ShowStep();
            Refresh();
        }

        public void Submit()
        {
            if (IsSolved)
                return;
            if (oneDigitAtATime)
            {
                SubmitDigit();
                return;
            }

            if (CurrentCode() != solution)
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

        void SubmitDigit()
        {
            if (m_Digits.Length == 0 || m_Step >= solution.Length)
                return;
            if (m_Digits[0].ToString()[0] != solution[m_Step])
            {
                Fail();
                return;
            }

            var last = m_Step == solution.Length - 1;
            if (last && !ArePrerequisitesMet)
            {
                SetFeedback(string.Format(lockedMessage, SolvedRequiredCount, requiredPuzzles.Length), lockedColor);
                return;
            }
            if (last)
            {
                Solve();
                return;
            }

            m_Step++;
            m_Digits[0] = 0;
            Refresh();
            SetFeedback(string.Format(digitOkMessage, m_Step + 1, solution.Length), successColor);
        }

        public override void ResetPuzzle()
        {
            base.ResetPuzzle();
            m_Step = 0;
            System.Array.Clear(m_Digits, 0, m_Digits.Length);
            foreach (var text in digitTexts)
                if (text != null)
                    text.color = Color.white;
            Refresh();
            ShowStep();
        }

        protected override void OnSolved()
        {
            SetFeedback(successMessage, successColor);
            foreach (var text in digitTexts)
                if (text != null)
                    text.color = successColor;
            if (oneDigitAtATime && digitTexts.Length > 0 && digitTexts[0] != null)
                digitTexts[0].text = solution; // le code complet s'affiche à la réussite
        }

        protected override void OnFailed()
        {
            SetFeedback(errorMessage, errorColor);
            StopAllCoroutines();
            StartCoroutine(Shake());
        }

        void ShowStep()
        {
            if (oneDigitAtATime && !IsSolved)
                SetFeedback(string.Format(stepMessage, m_Step + 1, DigitCount), Color.white);
            else
                SetFeedback("", Color.white);
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
