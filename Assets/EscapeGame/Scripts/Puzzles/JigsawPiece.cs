using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EscapeGame
{
    /// <summary>
    /// Pièce de puzzle (Image avec le PNG de la pièce). Se déplace en la visant avec le rayon et en maintenant
    /// la gâchette, et se verrouille quand elle est lâchée près de sa place.
    /// Elle suit le rayon dès qu'on appuie (sans seuil de glissement) et même si le rayon va vite :
    /// sa position est le point où le rayon traverse le plan de l'écran.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class JigsawPiece : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("Position du centre de la pièce quand elle est bien placée, par rapport au centre du cadre.")]
        public Vector2 targetInBoard;
        public bool hasTarget;

        public bool IsPlaced { get; private set; }

        JigsawPuzzle m_Puzzle;
        RectTransform m_Rect;
        Image m_Image;
        Vector3 m_StartPosition;
        Vector3 m_GrabOffset;
        bool m_Dragging;

        internal void Initialize(JigsawPuzzle puzzle)
        {
            m_Puzzle = puzzle;
            m_Rect = (RectTransform)transform;
            m_Image = GetComponent<Image>();
            m_StartPosition = m_Rect.localPosition;
            // Seules les parties visibles de la pièce sont cliquables (nécessite une texture "Read/Write").
            m_Image.alphaHitTestMinimumThreshold = 0.5f;
        }

        /// <summary>Place cible de la pièce, dans l'espace de son parent.</summary>
        public Vector3 TargetLocalPosition
        {
            get
            {
                var local = m_Rect.parent.InverseTransformPoint(m_Puzzle.BoardToWorld(targetInBoard));
                local.z = 0f;
                return local;
            }
        }

        // Prise au moment de l'appui : la pièce ne saute pas, même si le rayon a déjà bougé quand le glissement commence.
        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_Puzzle == null || IsPlaced || m_Puzzle.IsSolved || !TryGetPointer(eventData, out var pointer))
                return;
            m_Dragging = true;
            m_GrabOffset = m_Rect.localPosition - pointer;
            transform.SetAsLastSibling(); // passe au-dessus des autres pièces
        }

        // Pas de distance minimale avant de commencer à glisser : la pièce suit tout de suite.
        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!m_Dragging)
                OnPointerDown(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (m_Dragging && TryGetPointer(eventData, out var pointer))
                m_Rect.localPosition = ClampToParent(pointer + m_GrabOffset);
        }

        public void OnEndDrag(PointerEventData eventData) => Release();

        // Relâché sans avoir bougé (simple clic) : la prise s'arrête aussi.
        public void OnPointerUp(PointerEventData eventData) => Release();

        void Release()
        {
            if (!m_Dragging)
                return;
            m_Dragging = false;
            if (hasTarget && Vector2.Distance(m_Rect.localPosition, TargetLocalPosition) <= m_Puzzle.snapTolerance)
                Place();
        }

        public void ReturnToStart()
        {
            StopAllCoroutines();
            m_Dragging = false;
            IsPlaced = false;
            m_Rect.localScale = Vector3.one;
            m_Rect.localPosition = m_StartPosition;
            m_Image.raycastTarget = true;
        }

        void Place()
        {
            m_Rect.localPosition = TargetLocalPosition;
            IsPlaced = true;
            m_Image.raycastTarget = false; // verrouillée : ne gêne plus la saisie des autres pièces
            transform.SetAsFirstSibling(); // passe sous les pièces encore libres
            StopAllCoroutines();
            StartCoroutine(PlacedPulse());
            m_Puzzle.OnPiecePlaced();
        }

        // Petit "pop" + teinte verte : montre au joueur que la pièce vient de se mettre en place.
        // (La couleur de l'Image multiplie celle du PNG : on ne peut que teinter, pas éclaircir.)
        IEnumerator PlacedPulse()
        {
            const float duration = 0.4f;
            var color = m_Image.color;
            var tint = new Color(0.55f, 1f, 0.55f, color.a);
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = Mathf.Sin(t / duration * Mathf.PI); // 0 → 1 → 0
                m_Rect.localScale = Vector3.one * (1f + 0.1f * k);
                m_Image.color = Color.Lerp(color, tint, k);
                yield return null;
            }
            m_Rect.localScale = Vector3.one;
            m_Image.color = color;
        }

        // Point où le rayon (ou la souris) traverse le plan de l'écran, dans l'espace du parent de la pièce.
        // Ne dépend pas de ce que touche le rayon : un geste rapide ne fait pas "décrocher" la pièce.
        bool TryGetPointer(PointerEventData eventData, out Vector3 local)
        {
            local = default;
            var parent = m_Rect.parent;
            var plane = new Plane(parent.forward, parent.position);
            Vector3? world = null;

            if (eventData is TrackedDeviceEventData tracked && tracked.rayPoints != null && tracked.rayPoints.Count >= 2)
            {
                // Rayon de la manette : suite de segments (droit ou courbe).
                var points = tracked.rayPoints;
                for (var i = 0; i < points.Count - 1 && world == null; i++)
                {
                    var segment = points[i + 1] - points[i];
                    var ray = new Ray(points[i], segment);
                    if (plane.Raycast(ray, out var distance) && distance <= segment.magnitude)
                        world = ray.GetPoint(distance);
                }
            }
            else
            {
                var cam = eventData.pressEventCamera != null ? eventData.pressEventCamera : Camera.main;
                if (cam != null && plane.Raycast(cam.ScreenPointToRay(eventData.position), out var distance))
                    world = cam.ScreenPointToRay(eventData.position).GetPoint(distance);
            }

            if (world == null && eventData.pointerCurrentRaycast.gameObject != null)
                world = eventData.pointerCurrentRaycast.worldPosition;
            if (world == null)
                return false;

            local = parent.InverseTransformPoint(world.Value);
            local.z = 0f;
            return true;
        }

        Vector3 ClampToParent(Vector3 position)
        {
            var bounds = ((RectTransform)m_Rect.parent).rect;
            position.x = Mathf.Clamp(position.x, bounds.xMin, bounds.xMax);
            position.y = Mathf.Clamp(position.y, bounds.yMin, bounds.yMax);
            return position;
        }
    }
}
