using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Mot à écrire au feutre sur un tableau (WritableSurface), une lettre au-dessus de chaque trait "_".
    /// Quand toutes les cases sont remplies et que le feutre a quitté le tableau depuis quelques secondes,
    /// les lettres sont reconnues : mot juste → coche verte et lettres figées ; mot faux → les cases s'effacent.
    /// Les cases sont définies par les traits : chaque case est un rectangle posé sur son trait.
    /// </summary>
    public class BoardWordPuzzle : Puzzle
    {
        [Header("Mot")]
        [Tooltip("Mot attendu, une lettre majuscule par trait.")]
        public string solution = "SIX";
        public WritableSurface surface;
        [Tooltip("Traits \"_\" sous chaque lettre, de gauche à droite.")]
        public Transform[] slots = new Transform[0];
        [Tooltip("Taille (m) de la case au-dessus de chaque trait : largeur, hauteur.")]
        public Vector2 slotSize = new Vector2(0.16f, 0.2f);
        [Tooltip("Marge (m) autour des cases : un trait qui déborde un peu compte quand même, et est effacé avec la case.")]
        public float slotMargin = 0.03f;

        [Header("Vérification")]
        [Tooltip("Temps (s) sans écrire dans les cases avant de vérifier le mot.")]
        public float checkDelay = 2f;
        [Tooltip("Coche affichée quand le mot est juste.")]
        public GameObject successMark;
        [Tooltip("Affiche dans la Console les lettres reconnues (utile pour régler l'énigme).")]
        public bool logRecognition = true;

        const int k_MinPointsPerLetter = 4;

        List<LetterRecognizer.Point>[] m_Letters;
        int m_StrokeId;
        int m_CurrentSlot = -1;
        float m_LastInkTime;
        bool m_PendingCheck;

        void Awake()
        {
            m_Letters = new List<LetterRecognizer.Point>[slots.Length];
            for (var i = 0; i < m_Letters.Length; i++)
                m_Letters[i] = new List<LetterRecognizer.Point>();
            if (successMark != null)
                successMark.SetActive(false);
        }

        void OnEnable()
        {
            if (surface != null)
                surface.Stroked += OnStroked;
        }

        void OnDisable()
        {
            if (surface != null)
                surface.Stroked -= OnStroked;
        }

        void Update()
        {
            if (!m_PendingCheck || IsSolved || Time.time - m_LastInkTime < checkDelay)
                return;
            foreach (var letter in m_Letters)
                if (letter.Count < k_MinPointsPerLetter)
                    return;

            m_PendingCheck = false;
            CheckWord();
        }

        void OnStroked(Vector2 fromUV, Vector2 toUV, float radius, bool erase, bool newStroke)
        {
            if (IsSolved)
                return;

            var point = ToLocal(toUV);
            if (erase)
            {
                // L'effaceur dans une case efface toute sa lettre (la reconnaissance repart de zéro).
                for (var i = 0; i < slots.Length; i++)
                    if (m_Letters[i].Count > 0 && Overlaps(GetSlotRect(i), point, radius))
                        ClearSlot(i);
                return;
            }

            var slot = FindSlot(point);
            if (newStroke || slot != m_CurrentSlot)
            {
                m_StrokeId++;
                if (!newStroke && slot >= 0)
                    m_Letters[slot].Add(new LetterRecognizer.Point(ToLocal(fromUV), m_StrokeId));
            }
            m_CurrentSlot = slot;
            if (slot < 0)
                return;

            m_Letters[slot].Add(new LetterRecognizer.Point(point, m_StrokeId));
            m_LastInkTime = Time.time;
            m_PendingCheck = true;
        }

        void CheckWord()
        {
            var word = new StringBuilder();
            var details = new StringBuilder();
            for (var i = 0; i < m_Letters.Length; i++)
            {
                var letter = LetterRecognizer.Recognize(m_Letters[i], out var distance);
                word.Append(letter);
                details.Append($"{letter} ({distance:0.00})  ");
            }

            var success = string.Equals(word.ToString(), solution.Trim().ToUpperInvariant());
            if (logRecognition)
                Debug.Log($"{name} : lettres reconnues {details}→ {(success ? "juste" : "faux")}", this);

            if (success)
                Solve();
            else
            {
                for (var i = 0; i < slots.Length; i++)
                    ClearSlot(i);
                Fail();
            }
        }

        void ClearSlot(int index)
        {
            m_Letters[index].Clear();
            if (surface != null)
                surface.ClearRegion(GetSlotUVRect(index));
        }

        public override void ResetPuzzle()
        {
            base.ResetPuzzle();
            for (var i = 0; i < slots.Length; i++)
                ClearSlot(i);
            if (successMark != null)
                successMark.SetActive(false);
        }

        protected override void OnSolved()
        {
            // Les lettres restent : on fige leurs cases (plus d'encre ni d'effaceur dedans).
            for (var i = 0; i < slots.Length; i++)
                surface.Protect(GetSlotUVRect(i));
            if (successMark != null)
            {
                successMark.SetActive(true);
                StartCoroutine(Pop(successMark.transform));
            }
        }

        static IEnumerator Pop(Transform target)
        {
            const float duration = 0.35f;
            var scale = target.localScale;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = t / duration;
                target.localScale = scale * (k < 0.6f ? Mathf.Lerp(0f, 1.2f, k / 0.6f) : Mathf.Lerp(1.2f, 1f, (k - 0.6f) / 0.4f));
                yield return null;
            }
            target.localScale = scale;
        }

        // ---- Géométrie : tout est mesuré dans le repère de cet objet (en mètres, aligné sur le tableau) ----

        Vector2 ToLocal(Vector2 uv)
        {
            var world = surface.transform.TransformPoint(uv.x - 0.5f, uv.y - 0.5f, 0f);
            return transform.InverseTransformPoint(world);
        }

        /// <summary>Case au-dessus du trait <paramref name="index"/>, marge comprise, dans le repère de cet objet.</summary>
        public Rect GetSlotRect(int index)
        {
            var line = (Vector2)transform.InverseTransformPoint(slots[index].position);
            return new Rect(
                line.x - slotSize.x / 2f - slotMargin,
                line.y - slotMargin,
                slotSize.x + 2f * slotMargin,
                slotSize.y + 2f * slotMargin);
        }

        Rect GetSlotUVRect(int index)
        {
            var rect = GetSlotRect(index);
            var a = surface.WorldToUV(transform.TransformPoint(rect.min));
            var b = surface.WorldToUV(transform.TransformPoint(rect.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        int FindSlot(Vector2 point)
        {
            for (var i = 0; i < slots.Length; i++)
                if (GetSlotRect(i).Contains(point))
                    return i;
            return -1;
        }

        static bool Overlaps(Rect rect, Vector2 center, float radius) =>
            center.x + radius > rect.xMin && center.x - radius < rect.xMax &&
            center.y + radius > rect.yMin && center.y - radius < rect.yMax;

        void OnDrawGizmosSelected()
        {
            if (slots == null)
                return;
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    continue;
                var rect = GetSlotRect(i);
                Gizmos.DrawWireCube(rect.center, rect.size);
            }
        }
    }
}
