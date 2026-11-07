using System;
using System.Collections.Generic;

namespace MetinAR.Map
{
    /// <summary>
    /// Project-owned ear-clipping triangulator for simple polygons with holes (holes are bridged to the
    /// outer ring first). O(n²) worst case, adequate for street-level POC tiles. Input rings are interleaved
    /// (x, y) arrays in any winding; output indices reference the concatenated vertex list
    /// [exterior, hole0, hole1, ...] and are wound counter-clockwise in a y-up (east, north) frame.
    /// Degenerate input yields fewer (or zero) triangles rather than throwing.
    /// </summary>
    public static class Triangulator
    {
        public static List<int> Triangulate(double[] exterior, IReadOnlyList<double[]> holes, out double[] vertices)
        {
            int total = exterior.Length / 2;
            if (holes != null)
            {
                foreach (double[] h in holes)
                {
                    total += h.Length / 2;
                }
            }

            vertices = new double[total * 2];
            Array.Copy(exterior, vertices, exterior.Length);
            int offset = exterior.Length / 2;

            var outer = Ring(0, exterior.Length / 2, Geometry2D.SignedArea(exterior) < 0);
            var triangles = new List<int>();
            if (outer.Count < 3)
            {
                return triangles;
            }

            if (holes != null && holes.Count > 0)
            {
                var holeRings = new List<List<int>>();
                foreach (double[] h in holes)
                {
                    Array.Copy(h, 0, vertices, offset * 2, h.Length);
                    // Holes must be clockwise (opposite of the CCW outer ring).
                    List<int> ring = Ring(offset, h.Length / 2, Geometry2D.SignedArea(h) > 0);
                    offset += h.Length / 2;
                    if (ring.Count >= 3)
                    {
                        holeRings.Add(ring);
                    }
                }

                double[] verts = vertices;
                holeRings.Sort((a, b) => MaxX(verts, b).CompareTo(MaxX(verts, a)));
                foreach (List<int> hole in holeRings)
                {
                    Bridge(vertices, outer, hole);
                }
            }

            EarClip(vertices, outer, triangles);
            return triangles;
        }

        private static List<int> Ring(int start, int count, bool reverse)
        {
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(start + (reverse ? count - 1 - i : i));
            }

            return list;
        }

        private static double MaxX(double[] v, List<int> ring)
        {
            double m = double.MinValue;
            foreach (int i in ring)
            {
                m = Math.Max(m, v[i * 2]);
            }

            return m;
        }

        /// <summary>Connects a hole to the outer ring through a mutually visible vertex pair.</summary>
        private static void Bridge(double[] v, List<int> outer, List<int> hole)
        {
            int hi = 0;
            for (int i = 1; i < hole.Count; i++)
            {
                if (v[hole[i] * 2] > v[hole[hi] * 2])
                {
                    hi = i;
                }
            }

            double hx = v[hole[hi] * 2];
            double hy = v[hole[hi] * 2 + 1];
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                double ox = v[outer[i] * 2];
                double oy = v[outer[i] * 2 + 1];
                if (ox < hx)
                {
                    continue;
                }

                double d = (ox - hx) * (ox - hx) + (oy - hy) * (oy - hy);
                if (d < bestDist && Visible(v, outer, hole, hx, hy, ox, oy))
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (best < 0)
            {
                // Fallback: nearest outer vertex regardless of visibility (rare, may create overlap).
                for (int i = 0; i < outer.Count; i++)
                {
                    double ox = v[outer[i] * 2];
                    double oy = v[outer[i] * 2 + 1];
                    double d = (ox - hx) * (ox - hx) + (oy - hy) * (oy - hy);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }
            }

            var merged = new List<int>(outer.Count + hole.Count + 2);
            for (int i = 0; i <= best; i++)
            {
                merged.Add(outer[i]);
            }

            for (int k = 0; k <= hole.Count; k++)
            {
                merged.Add(hole[(hi + k) % hole.Count]);
            }

            merged.Add(outer[best]);
            for (int i = best + 1; i < outer.Count; i++)
            {
                merged.Add(outer[i]);
            }

            outer.Clear();
            outer.AddRange(merged);
        }

        private static bool Visible(double[] v, List<int> outer, List<int> hole, double ax, double ay, double bx, double by)
        {
            return !CrossesAny(v, outer, ax, ay, bx, by) && !CrossesAny(v, hole, ax, ay, bx, by);
        }

        private static bool CrossesAny(double[] v, List<int> ring, double ax, double ay, double bx, double by)
        {
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double cx = v[ring[j] * 2], cy = v[ring[j] * 2 + 1];
                double dx = v[ring[i] * 2], dy = v[ring[i] * 2 + 1];
                if ((cx == ax && cy == ay) || (dx == ax && dy == ay) || (cx == bx && cy == by) || (dx == bx && dy == by))
                {
                    continue;
                }

                if (SegmentsIntersect(ax, ay, bx, by, cx, cy, dx, dy))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay);
            double d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy);
            double d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(double ox, double oy, double ax, double ay, double bx, double by)
        {
            return (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);
        }

        private static void EarClip(double[] v, List<int> ring, List<int> triangles)
        {
            var idx = new List<int>(ring);
            RemoveDuplicates(v, idx);
            int guard = idx.Count * idx.Count + 16;
            int i = 0;
            while (idx.Count > 3 && guard-- > 0)
            {
                int n = idx.Count;
                int a = idx[(i + n - 1) % n];
                int b = idx[i % n];
                int c = idx[(i + 1) % n];
                if (IsEar(v, idx, a, b, c))
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    idx.RemoveAt(i % n);
                    i = Math.Max(0, (i % n) - 1);
                    continue;
                }

                if (Cross(v[a * 2], v[a * 2 + 1], v[b * 2], v[b * 2 + 1], v[c * 2], v[c * 2 + 1]) == 0)
                {
                    // Collinear / zero-area vertex: drop it.
                    idx.RemoveAt(i % n);
                    continue;
                }

                i = (i + 1) % idx.Count;
            }

            if (idx.Count == 3)
            {
                double area = Cross(v[idx[0] * 2], v[idx[0] * 2 + 1], v[idx[1] * 2], v[idx[1] * 2 + 1], v[idx[2] * 2], v[idx[2] * 2 + 1]);
                if (area > 0)
                {
                    triangles.Add(idx[0]);
                    triangles.Add(idx[1]);
                    triangles.Add(idx[2]);
                }
            }
        }

        private static void RemoveDuplicates(double[] v, List<int> idx)
        {
            for (int k = idx.Count - 1; k >= 0 && idx.Count > 0; k--)
            {
                int next = idx[(k + 1) % idx.Count];
                if (k < idx.Count && v[idx[k] * 2] == v[next * 2] && v[idx[k] * 2 + 1] == v[next * 2 + 1] && idx[k] != next)
                {
                    idx.RemoveAt(k);
                }
            }
        }

        private static bool IsEar(double[] v, List<int> idx, int a, int b, int c)
        {
            double ax = v[a * 2], ay = v[a * 2 + 1];
            double bx = v[b * 2], by = v[b * 2 + 1];
            double cx = v[c * 2], cy = v[c * 2 + 1];
            if (Cross(ax, ay, bx, by, cx, cy) <= 0)
            {
                return false;
            }

            foreach (int p in idx)
            {
                if (p == a || p == b || p == c)
                {
                    continue;
                }

                double px = v[p * 2], py = v[p * 2 + 1];
                if ((px == ax && py == ay) || (px == bx && py == by) || (px == cx && py == cy))
                {
                    continue;
                }

                if (Cross(ax, ay, bx, by, px, py) >= 0 && Cross(bx, by, cx, cy, px, py) >= 0 && Cross(cx, cy, ax, ay, px, py) >= 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
