using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EscapeGame
{
    /// <summary>
    /// Zone sur laquelle un WritingTool (feutre, effaceur) peut écrire.
    /// À placer sur un Quad : l'encre est dessinée dans une texture propre à la zone,
    /// affichée par-dessus un fond semi-transparent. Le collider doit être en Trigger
    /// pour que le feutre puisse le traverser sans être repoussé.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WritableSurface : MonoBehaviour
    {
        [Tooltip("Résolution de l'encre, en pixels par mètre de surface.")]
        public int pixelsPerMeter = 1000;
        [Tooltip("Couleur et opacité du fond de la zone (alpha 0 = invisible).")]
        public Color backgroundColor = new Color(1f, 1f, 1f, 0.25f);

        [SerializeField] Shader m_SurfaceShader;
        [SerializeField] Shader m_BrushShader;

        static readonly int k_InkTex = Shader.PropertyToID("_InkTex");
        static readonly int k_BackgroundColor = Shader.PropertyToID("_BackgroundColor");
        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_Softness = Shader.PropertyToID("_Softness");
        static readonly int k_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int k_DstBlend = Shader.PropertyToID("_DstBlend");

        RenderTexture m_Ink;
        Material m_SurfaceMaterial;
        Material m_BrushMaterial;

        readonly List<Rect> m_ProtectedRegions = new List<Rect>();

        static readonly List<WritableSurface> s_Active = new List<WritableSurface>();

        /// <summary>Zones d'écriture actives dans la scène.</summary>
        public static IReadOnlyList<WritableSurface> Active => s_Active;

        /// <summary>
        /// Appelé à chaque trait tracé (from, to en coordonnées 0-1 ; rayon en mètres ; effacement ; début d'un nouveau trait).
        /// </summary>
        public event Action<Vector2, Vector2, float, bool, bool> Stroked;

        /// <summary>Taille de la zone dans le monde, en mètres.</summary>
        public Vector2 Size => new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

        void Awake()
        {
            var size = Size;
            m_Ink = new RenderTexture(
                Mathf.Clamp(Mathf.RoundToInt(size.x * pixelsPerMeter), 64, 4096),
                Mathf.Clamp(Mathf.RoundToInt(size.y * pixelsPerMeter), 64, 4096),
                0, RenderTextureFormat.ARGB32)
            {
                name = name + " (encre)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            m_Ink.Create();

            m_SurfaceMaterial = new Material(m_SurfaceShader) { name = name + " (surface)" };
            m_SurfaceMaterial.SetTexture(k_InkTex, m_Ink);
            m_SurfaceMaterial.SetColor(k_BackgroundColor, backgroundColor);
            GetComponent<MeshRenderer>().sharedMaterial = m_SurfaceMaterial;

            m_BrushMaterial = new Material(m_BrushShader) { name = name + " (pinceau)" };

            Clear();
        }

        void OnEnable() => s_Active.Add(this);
        void OnDisable() => s_Active.Remove(this);

        void OnDestroy()
        {
            if (m_Ink != null)
                m_Ink.Release();
            Destroy(m_Ink);
            Destroy(m_SurfaceMaterial);
            Destroy(m_BrushMaterial);
        }

        void OnValidate()
        {
            if (m_SurfaceMaterial != null)
                m_SurfaceMaterial.SetColor(k_BackgroundColor, backgroundColor);
        }

        void Reset()
        {
            m_SurfaceShader = Shader.Find("EscapeGame/WritableSurface");
            m_BrushShader = Shader.Find("Hidden/EscapeGame/BrushStamp");
        }

        /// <summary>Convertit un point du monde en coordonnées de texture (0-1) sur le Quad.</summary>
        public Vector2 WorldToUV(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            return new Vector2(local.x + 0.5f, local.y + 0.5f);
        }

        /// <summary>
        /// Indique si <paramref name="tipPoint"/> a traversé la surface (dans les limites de la zone),
        /// et de combien il faut repousser l'outil pour ramener la pointe pile sur la surface.
        /// </summary>
        /// <param name="toolPosition">Position actuelle de l'outil : définit le côté d'où il vient.</param>
        public bool TryGetPenetration(Vector3 toolPosition, Vector3 tipPoint, float range, out Vector3 push)
        {
            push = Vector3.zero;
            var normal = transform.forward;
            var origin = transform.position;

            var toolDistance = Vector3.Dot(toolPosition - origin, normal);
            if (Mathf.Abs(toolDistance) > range)
                return false;

            var side = toolDistance >= 0f ? 1f : -1f;
            var tipDistance = Vector3.Dot(tipPoint - origin, normal) * side;
            if (tipDistance >= 0f)
                return false;

            var local = transform.InverseTransformPoint(tipPoint);
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
                return false;

            push = normal * (side * -tipDistance);
            return true;
        }

        [ContextMenu("Tout effacer")]
        public void Clear()
        {
            if (m_Ink == null)
                return;
            var previous = RenderTexture.active;
            RenderTexture.active = m_Ink;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        /// <summary>
        /// Trace un trait continu de <paramref name="from"/> à <paramref name="to"/> (coordonnées 0-1)
        /// en posant des tampons ronds rapprochés.
        /// </summary>
        /// <param name="newStroke">Premier point d'un trait (la pointe vient de toucher la surface).</param>
        public void DrawLine(Vector2 from, Vector2 to, Color color, float radius, float softness, bool erase, bool newStroke = false)
        {
            if (m_Ink == null)
                return;
            Stroked?.Invoke(from, to, radius, erase, newStroke);

            var size = Size;
            var radiusUV = new Vector2(radius / size.x, radius / size.y);
            var distance = Vector2.Scale(to - from, size).magnitude;
            var spacing = Mathf.Max(radius * 0.25f, 0.0005f);
            var steps = Mathf.Clamp(Mathf.CeilToInt(distance / spacing), 1, 512);

            m_BrushMaterial.SetColor(k_Color, color);
            m_BrushMaterial.SetFloat(k_Softness, Mathf.Max(softness, 0.01f));
            // Encre : on ajoute de la couleur. Effaceur : on retire l'encre existante.
            m_BrushMaterial.SetInt(k_SrcBlend, (int)(erase ? BlendMode.Zero : BlendMode.One));
            m_BrushMaterial.SetInt(k_DstBlend, (int)BlendMode.OneMinusSrcAlpha);

            var previous = RenderTexture.active;
            RenderTexture.active = m_Ink;
            GL.PushMatrix();
            GL.LoadOrtho();
            m_BrushMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            // Le point de départ a déjà été tamponné à l'image précédente, sauf pour un simple point.
            for (var i = from == to ? 0 : 1; i <= steps; i++)
            {
                var center = Vector2.Lerp(from, to, i / (float)steps);
                if (!IsProtected(center, radiusUV))
                    Stamp(center, radiusUV);
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        /// <summary>Efface toute l'encre dans un rectangle (coordonnées 0-1).</summary>
        public void ClearRegion(Rect uvRect)
        {
            if (m_Ink == null)
                return;

            m_BrushMaterial.SetColor(k_Color, Color.white);
            m_BrushMaterial.SetFloat(k_Softness, 1f);
            m_BrushMaterial.SetInt(k_SrcBlend, (int)BlendMode.Zero);
            m_BrushMaterial.SetInt(k_DstBlend, (int)BlendMode.OneMinusSrcAlpha);

            var previous = RenderTexture.active;
            RenderTexture.active = m_Ink;
            GL.PushMatrix();
            GL.LoadOrtho();
            m_BrushMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            // Tous les sommets au centre du tampon : le pinceau efface à 100 % sur tout le rectangle.
            GL.TexCoord2(0.5f, 0.5f);
            GL.Vertex3(uvRect.xMin, uvRect.yMin, 0f);
            GL.Vertex3(uvRect.xMin, uvRect.yMax, 0f);
            GL.Vertex3(uvRect.xMax, uvRect.yMax, 0f);
            GL.Vertex3(uvRect.xMax, uvRect.yMin, 0f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        /// <summary>Fige un rectangle (coordonnées 0-1) : le feutre et l'effaceur n'y ont plus d'effet.</summary>
        public void Protect(Rect uvRect) => m_ProtectedRegions.Add(uvRect);

        bool IsProtected(Vector2 center, Vector2 radius)
        {
            foreach (var region in m_ProtectedRegions)
                if (center.x + radius.x > region.xMin && center.x - radius.x < region.xMax &&
                    center.y + radius.y > region.yMin && center.y - radius.y < region.yMax)
                    return true;
            return false;
        }

        static void Stamp(Vector2 center, Vector2 radius)
        {
            GL.TexCoord2(0f, 0f); GL.Vertex3(center.x - radius.x, center.y - radius.y, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(center.x - radius.x, center.y + radius.y, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(center.x + radius.x, center.y + radius.y, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(center.x + radius.x, center.y - radius.y, 0f);
        }
    }
}
