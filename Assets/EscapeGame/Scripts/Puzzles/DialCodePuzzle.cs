using System.Collections;
using TMPro;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Cadenas à molettes : un chiffre par colonne, modifié avec les flèches haut / bas, puis "Valider".
    /// Si des énigmes requises (Required Puzzles) ne sont pas résolues, le bon code est refusé.
    /// </summary>
    public class DialCodePuzzle : Puzzle
    {
        [Header("Code")]
        public string solution = "976";

        [Header("Affichage")]
        [Tooltip("Un texte par chiffre, de gauche à droite.")]
        public TMP_Text[] digitTexts = new TMP_Text[0];
        public TMP_Text feedback;
        public string successMessage = "Déverrouillé !";
        public string errorMessage = "Code incorrect";
        public string lockedMessage = "Il reste des énigmes à résoudre ({0}/{1})";
        public Color successColor = new Color(0.3f, 0.9f, 0.4f);
        public Color errorColor = new Color(1f, 0.35f, 0.35f);
        public Color lockedColor = new Color(1f, 0.75f, 0.3f);

        int[] m_Digits;

        void Awake()
        {
            m_Digits = new int[digitTexts.Length];
        }

        void Start()
        {
            Refresh();
            SetFeedback("", Color.white);
        }

        public void Increment(int index) => Change(index, 1);
        public void Decrement(int index) => Change(index, -1);

        void Change(int index, int delta)
        {
            if (IsSolved || index < 0 || index >= m_Digits.Length)
                return;
            m_Digits[index] = (m_Digits[index] + delta + 10) % 10;
            SetFeedback("", Color.white);
            Refresh();
        }

        public void Submit()
        {
            if (IsSolved)
                return;

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

        public override void ResetPuzzle()
        {
            base.ResetPuzzle();
            System.Array.Clear(m_Digits, 0, m_Digits.Length);
            foreach (var text in digitTexts)
                if (text != null)
                    text.color = Color.white;
            Refresh();
            SetFeedback("", Color.white);
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
