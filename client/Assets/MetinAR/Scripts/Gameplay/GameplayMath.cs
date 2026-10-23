using System;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Rounding helpers used by the METINAR-017 formulas (docs/design). They mirror the reference generator
    /// docs/design/tables/generate_tables.py: Python's round() is round-half-to-even, which is also the
    /// default MidpointRounding of System.Math.Round, so table values are reproduced exactly.
    /// </summary>
    public static class GameplayMath
    {
        /// <summary>Round to the nearest integer, ties to even (Python <c>round(x)</c>).</summary>
        public static int RoundToInt(double value)
        {
            return checked((int)Math.Round(value, MidpointRounding.ToEven));
        }

        /// <summary>Round to the nearest multiple of <paramref name="step"/> (generator <c>round_to</c>; spec round5/round10).</summary>
        public static int RoundTo(double value, int step)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            return checked(step * (int)Math.Round(value / step, MidpointRounding.ToEven));
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp01(double value)
        {
            return Clamp(value, 0.0, 1.0);
        }
    }
}
