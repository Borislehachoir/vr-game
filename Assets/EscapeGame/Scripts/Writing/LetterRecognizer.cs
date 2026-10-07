using System.Collections.Generic;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Reconnaissance de lettres majuscules manuscrites, avec l'algorithme "$P" (Vatavu, Anthony, Wobbrock, 2012) :
    /// le tracé est comparé, comme un nuage de points, à un modèle de chaque lettre de l'alphabet.
    /// L'ordre et le sens des traits n'ont donc pas d'importance.
    /// </summary>
    public static class LetterRecognizer
    {
        /// <summary>Point d'un tracé : position et numéro du trait auquel il appartient.</summary>
        public struct Point
        {
            public Vector2 position;
            public int stroke;

            public Point(Vector2 position, int stroke)
            {
                this.position = position;
                this.stroke = stroke;
            }
        }

        const int k_SampleCount = 32;

        struct Template
        {
            public char letter;
            public Vector2[] cloud;
        }

        static List<Template> s_Templates;

        /// <summary>
        /// Lettre la plus ressemblante au tracé. <paramref name="distance"/> mesure l'écart avec le modèle
        /// (0 = identique ; plus elle est grande, moins le tracé ressemble à la lettre).
        /// </summary>
        public static char Recognize(IReadOnlyList<Point> points, out float distance)
        {
            distance = float.MaxValue;
            if (points == null || points.Count < 2)
                return '?';

            s_Templates ??= BuildTemplates();
            var candidate = Normalize(points);
            var best = '?';
            foreach (var template in s_Templates)
            {
                var d = CloudDistance(candidate, template.cloud, distance);
                if (d < distance)
                {
                    distance = d;
                    best = template.letter;
                }
            }
            return best;
        }

        // ---- $P ----

        static Vector2[] Normalize(IReadOnlyList<Point> points)
        {
            var cloud = Resample(points, k_SampleCount);

            // Mise à l'échelle uniforme (garde les proportions : un I reste fin) puis centrage.
            var min = cloud[0];
            var max = cloud[0];
            foreach (var p in cloud)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var scale = Mathf.Max(max.x - min.x, max.y - min.y);
            if (scale < 1e-6f)
                scale = 1f;

            var centroid = Vector2.zero;
            for (var i = 0; i < cloud.Length; i++)
            {
                cloud[i] = (cloud[i] - min) / scale;
                centroid += cloud[i];
            }
            centroid /= cloud.Length;
            for (var i = 0; i < cloud.Length; i++)
                cloud[i] -= centroid;
            return cloud;
        }

        // Répartit n points à intervalles réguliers le long des traits (sans relier deux traits différents).
        static Vector2[] Resample(IReadOnlyList<Point> points, int n)
        {
            var length = 0f;
            for (var i = 1; i < points.Count; i++)
                if (points[i].stroke == points[i - 1].stroke)
                    length += Vector2.Distance(points[i - 1].position, points[i].position);

            var result = new Vector2[n];
            result[0] = points[0].position;
            var count = 1;
            if (length < 1e-6f)
            {
                for (; count < n; count++)
                    result[count] = points[0].position;
                return result;
            }

            var interval = length / (n - 1);
            var accumulated = 0f;
            var previous = points[0];
            for (var i = 1; i < points.Count && count < n; i++)
            {
                var current = points[i];
                if (current.stroke != previous.stroke)
                {
                    previous = current;
                    continue;
                }

                var d = Vector2.Distance(previous.position, current.position);
                if (accumulated + d >= interval && d > 0f)
                {
                    var t = (interval - accumulated) / d;
                    var q = new Point(Vector2.Lerp(previous.position, current.position, t), current.stroke);
                    result[count++] = q.position;
                    accumulated = 0f;
                    previous = q;
                    i--; // le point courant reste à traiter
                }
                else
                {
                    accumulated += d;
                    previous = current;
                }
            }

            // Arrondis de calcul : complète avec le dernier point.
            for (; count < n; count++)
                result[count] = points[points.Count - 1].position;
            return result;
        }

        static float CloudDistance(Vector2[] a, Vector2[] b, float bestSoFar)
        {
            var step = Mathf.Max(1, Mathf.FloorToInt(Mathf.Sqrt(a.Length)));
            var min = float.MaxValue;
            for (var start = 0; start < a.Length; start += step)
            {
                min = Mathf.Min(min, GreedyMatch(a, b, start, Mathf.Min(min, bestSoFar)));
                min = Mathf.Min(min, GreedyMatch(b, a, start, Mathf.Min(min, bestSoFar)));
            }
            return min;
        }

        static float GreedyMatch(Vector2[] a, Vector2[] b, int start, float cutoff)
        {
            var n = a.Length;
            var matched = new bool[n];
            var sum = 0f;
            var i = start;
            do
            {
                var index = -1;
                var min = float.MaxValue;
                for (var j = 0; j < n; j++)
                {
                    if (matched[j])
                        continue;
                    var d = Vector2.Distance(a[i], b[j]);
                    if (d < min)
                    {
                        min = d;
                        index = j;
                    }
                }
                matched[index] = true;
                // Les premiers points appariés comptent plus : ils ont eu le plus de choix.
                var weight = 1f - ((i - start + n) % n) / (float)n;
                sum += weight * min;
                if (sum >= cutoff)
                    return sum;
                i = (i + 1) % n;
            } while (i != start);
            return sum;
        }

        // ---- Modèles des lettres (repère : x vers la droite, y vers le haut, hauteur 1) ----

        static List<Template> BuildTemplates()
        {
            var templates = new List<Template>();

            void Add(char letter, params List<Vector2>[] strokes)
            {
                var points = new List<Point>();
                for (var s = 0; s < strokes.Length; s++)
                    foreach (var p in strokes[s])
                        points.Add(new Point(p, s));
                templates.Add(new Template { letter = letter, cloud = Normalize(points) });
            }

            Add('A', Line(0, 0, .35f, 1, .7f, 0), Line(.15f, .4f, .55f, .4f));
            Add('A', Line(0, 0, .35f, 1), Line(.35f, 1, .7f, 0), Line(.15f, .4f, .55f, .4f));
            Add('B', Line(0, 0, 0, 1), Join(Line(0, 1, .35f, 1), Arc(.35f, .75f, .25f, .25f, 90, -90), Line(.35f, .5f, 0, .5f)),
                Join(Line(0, .5f, .4f, .5f), Arc(.4f, .25f, .25f, .25f, 90, -90), Line(.4f, 0, 0, 0)));
            Add('C', Arc(.4f, .5f, .4f, .5f, 50, 310));
            Add('D', Line(0, 0, 0, 1), Join(Line(0, 1, .3f, 1), Arc(.3f, .5f, .4f, .5f, 90, -90), Line(.3f, 0, 0, 0)));
            Add('E', Line(.6f, 1, 0, 1, 0, 0, .6f, 0), Line(0, .5f, .45f, .5f));
            Add('E', Line(0, 0, 0, 1), Line(0, 1, .6f, 1), Line(0, .5f, .45f, .5f), Line(0, 0, .6f, 0));
            Add('F', Line(.6f, 1, 0, 1, 0, 0), Line(0, .5f, .45f, .5f));
            Add('F', Line(0, 0, 0, 1), Line(0, 1, .6f, 1), Line(0, .5f, .45f, .5f));
            Add('G', Join(Arc(.4f, .5f, .4f, .5f, 50, 360), Line(.8f, .5f, .45f, .5f)));
            Add('H', Line(0, 0, 0, 1), Line(.7f, 0, .7f, 1), Line(0, .5f, .7f, .5f));
            Add('I', Line(0, 1, 0, 0));
            Add('I', Line(0, 1, 0, 0), Line(-.2f, 1, .2f, 1), Line(-.2f, 0, .2f, 0));
            Add('J', Join(Line(.5f, 1, .5f, .3f), Arc(.25f, .3f, .25f, .3f, 0, -180)));
            Add('K', Line(0, 0, 0, 1), Line(.6f, 1, 0, .45f, .6f, 0));
            Add('K', Line(0, 0, 0, 1), Line(.6f, 1, 0, .45f), Line(.15f, .55f, .6f, 0));
            Add('L', Line(0, 1, 0, 0, .6f, 0));
            Add('M', Line(0, 0, 0, 1, .4f, .4f, .8f, 1, .8f, 0));
            Add('N', Line(0, 0, 0, 1, .7f, 0, .7f, 1));
            Add('O', Arc(.4f, .5f, .4f, .5f, 90, 450));
            Add('P', Line(0, 0, 0, 1), Join(Line(0, 1, .35f, 1), Arc(.35f, .75f, .25f, .25f, 90, -90), Line(.35f, .5f, 0, .5f)));
            Add('Q', Arc(.4f, .5f, .4f, .5f, 90, 450), Line(.5f, .2f, .85f, -.1f));
            Add('R', Line(0, 0, 0, 1), Join(Line(0, 1, .35f, 1), Arc(.35f, .75f, .25f, .25f, 90, -90), Line(.35f, .5f, 0, .5f)),
                Line(.25f, .5f, .65f, 0));
            Add('S', Join(Arc(.3f, .75f, .3f, .25f, 30, 270), Arc(.3f, .25f, .3f, .25f, 90, -150)));
            Add('T', Line(0, 1, .7f, 1), Line(.35f, 1, .35f, 0));
            Add('U', Join(Line(0, 1, 0, .35f), Arc(.35f, .35f, .35f, .35f, 180, 360), Line(.7f, .35f, .7f, 1)));
            Add('V', Line(0, 1, .35f, 0, .7f, 1));
            Add('W', Line(0, 1, .2f, 0, .45f, .6f, .7f, 0, .9f, 1));
            Add('X', Line(0, 1, .7f, 0), Line(.7f, 1, 0, 0));
            Add('Y', Line(0, 1, .35f, .5f), Line(.7f, 1, .35f, .5f), Line(.35f, .5f, .35f, 0));
            Add('Z', Line(0, 1, .7f, 1, 0, 0, .7f, 0));
            return templates;
        }

        // Ligne brisée passant par les points (x0, y0, x1, y1, ...).
        static List<Vector2> Line(params float[] xy)
        {
            var points = new List<Vector2>();
            for (var i = 0; i + 1 < xy.Length; i += 2)
                points.Add(new Vector2(xy[i], xy[i + 1]));
            return points;
        }

        // Arc d'ellipse de l'angle a0 à a1 (degrés, sens trigonométrique si a1 > a0).
        static List<Vector2> Arc(float cx, float cy, float rx, float ry, float a0, float a1)
        {
            const int segments = 16;
            var points = new List<Vector2>();
            for (var i = 0; i <= segments; i++)
            {
                var a = Mathf.Lerp(a0, a1, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a)));
            }
            return points;
        }

        // Enchaîne plusieurs morceaux en un seul trait.
        static List<Vector2> Join(params List<Vector2>[] parts)
        {
            var points = new List<Vector2>();
            foreach (var part in parts)
                points.AddRange(part);
            return points;
        }
    }
}
