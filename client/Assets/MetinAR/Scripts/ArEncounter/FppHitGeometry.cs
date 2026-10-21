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
        /// <summary>Spider body footprint (mob-local metres before the visual scale): abdomen + cephalothorax plus 2.5 cm.</summary>
        public static readonly FppPoint BodyMin = new FppPoint(-0.285, 0.125, -0.605);
        /// <summary>Top of the literal body box. Kept for reference: on a phone held at chest height the blade passes above it.</summary>
        public const double BodyTopY = 0.595;

        /// <summary>
        /// #31 v0 A31 P0-1: the spider stands on the floor, the virtual sword is held at the phone (about 1.2–1.5 m above the
        /// floor on a handheld device, 2.9 m above the detected plane in the A31 session). A literal 3D blade-versus-body test
        /// therefore never hits at the natural 20–40° phone pitch, at any distance. The hit volume is the body footprint
        /// extruded up to <see cref="ReachColumnTopY"/> (mob-local), so only the horizontal reach (about 1.25–1.4 m from the
        /// camera to the spider centre, see MOBS_NPC "dystans walki 1,2 m") decides a hit.
        /// </summary>
        public const double ReachColumnTopY = 3.5;

        /// <summary>Hit volume used by FPP combat: body footprint extruded vertically (mob-local frame).</summary>
        public static FppPoint ReachColumnMax => new FppPoint(0.285, ReachColumnTopY, 0.345);

        /// <summary>Blade segment against the vertical reach column of the body footprint (mob-local frame).</summary>
        public static bool IntersectsReachColumn(FppPoint a, FppPoint b) => IntersectsBox(a, b, BodyMin, ReachColumnMax);

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
