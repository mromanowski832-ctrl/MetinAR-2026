using System;

namespace MetinAR.ArEncounter
{
    public readonly struct FppPoint
    {
        public readonly double X, Y, Z;
        public FppPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public bool IsFinite => Valid(X) && Valid(Y) && Valid(Z);
        private static bool Valid(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    /// <summary>Blade segment against a body box, all in the target's local coordinate frame.</summary>
    public static class FppHitGeometry
    {
        public static bool IntersectsBox(FppPoint a, FppPoint b, FppPoint min, FppPoint max)
        {
            if (!a.IsFinite || !b.IsFinite || !min.IsFinite || !max.IsFinite
                || min.X > max.X || min.Y > max.Y || min.Z > max.Z) return false;
            double enter = 0, exit = 1;
            return Slab(a.X, b.X - a.X, min.X, max.X, ref enter, ref exit)
                && Slab(a.Y, b.Y - a.Y, min.Y, max.Y, ref enter, ref exit)
                && Slab(a.Z, b.Z - a.Z, min.Z, max.Z, ref enter, ref exit);
        }

        private static bool Slab(double p, double d, double min, double max, ref double enter, ref double exit)
        {
            if (double.IsInfinity(d) || double.IsNaN(d)) return false;
            if (Math.Abs(d) < 1e-12) return p >= min && p <= max;
            double t0 = (min - p) / d, t1 = (max - p) / d;
            if (t0 > t1) { double swap = t0; t0 = t1; t1 = swap; }
            enter = Math.Max(enter, t0);
            exit = Math.Min(exit, t1);
            return enter <= exit;
        }
    }
}
