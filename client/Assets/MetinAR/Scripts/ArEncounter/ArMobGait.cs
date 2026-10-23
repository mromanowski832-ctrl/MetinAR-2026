using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// #97 rule 5: procedural walk cycle of the prototype spider while it moves by itself (no animation clips exist).
    /// Alternating tetrapod gait: legs L0, R1, L2, R3 swing in phase, the other four in anti-phase; the body bobs twice
    /// per cycle. The phase comes from the distance travelled, so the legs never "skate". Visual only: hit geometry uses
    /// the mob root. No UnityEngine dependency.
    /// </summary>
    public static class ArMobGait
    {
        /// <summary>Metres travelled per full gait cycle (PROTOTYPE).</summary>
        public const double StrideMeters = 0.22;

        /// <summary>Peak forward/back swing of an upper leg segment (degrees, PROTOTYPE).</summary>
        public const double SwingDegrees = 18.0;

        /// <summary>Peak lift of a swinging leg (metres, unscaled model, PROTOTYPE).</summary>
        public const double LiftMeters = 0.035;

        /// <summary>Peak vertical body bob (metres, unscaled model, PROTOTYPE).</summary>
        public const double BobMeters = 0.012;

        /// <summary>Gait phase in radians for a travelled distance.</summary>
        public static double Phase(double travelledMeters)
        {
            return travelledMeters / StrideMeters * 2.0 * Math.PI;
        }

        /// <summary>Leg set of leg <paramref name="index"/> (0–3) on side <paramref name="left"/>: true = set A (L0, R1, L2, R3).</summary>
        public static bool IsSetA(bool left, int index)
        {
            return ((index & 1) == 0) == left;
        }

        /// <summary>Forward swing (degrees) of a leg; <paramref name="weight"/> 0–1 fades the gait in/out with speed.</summary>
        public static double LegSwing(double phase, bool left, int index, double weight)
        {
            double p = IsSetA(left, index) ? phase : phase + Math.PI;
            return Math.Sin(p) * SwingDegrees * Clamp01(weight);
        }

        /// <summary>Lift (metres, unscaled) of a leg: only in the forward-swing half of its cycle, so one tetrapod always stands.</summary>
        public static double LegLift(double phase, bool left, int index, double weight)
        {
            double p = IsSetA(left, index) ? phase : phase + Math.PI;
            return Math.Max(0.0, Math.Cos(p)) * LiftMeters * Clamp01(weight);
        }

        /// <summary>Body bob (metres, unscaled).</summary>
        public static double Bob(double phase, double weight)
        {
            return Math.Abs(Math.Sin(phase)) * BobMeters * Clamp01(weight);
        }

        private static double Clamp01(double v)
        {
            return v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
        }
    }
}
