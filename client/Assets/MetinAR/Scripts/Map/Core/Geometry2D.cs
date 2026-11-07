using System;

namespace MetinAR.Map
{
    /// <summary>Pure 2D helpers on interleaved (x, y) double arrays.</summary>
    public static class Geometry2D
    {
        public static double SegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + t * dx - px;
            double cy = ay + t * dy - py;
            return cx * cx + cy * cy;
        }

        /// <summary>Squared distance from a point to a closed ring's edges.</summary>
        public static double RingDistanceSquared(double[] ring, double px, double py)
        {
            double best = double.MaxValue;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double d = SegmentDistanceSquared(px, py, ring[j * 2], ring[j * 2 + 1], ring[i * 2], ring[i * 2 + 1]);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary>Even-odd test on one ring.</summary>
        public static bool PointInRing(double[] ring, double px, double py)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[i * 2], yi = ring[i * 2 + 1];
                double xj = ring[j * 2], yj = ring[j * 2 + 1];
                if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Inside exterior and not inside any hole.</summary>
        public static bool PointInPolygon(ProjectedPolygon polygon, double px, double py)
        {
            if (!PointInRing(polygon.Exterior, px, py))
            {
                return false;
            }

            foreach (double[] hole in polygon.Holes)
            {
                if (PointInRing(hole, px, py))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Signed area of an interleaved ring (counter-clockwise positive in a y-up frame).</summary>
        public static double SignedArea(double[] ring)
        {
            double sum = 0;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                sum += ring[j * 2] * ring[i * 2 + 1] - ring[i * 2] * ring[j * 2 + 1];
            }

            return sum / 2.0;
        }
    }
}
