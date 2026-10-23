using System;
using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Lead decision 2026-10-23 04:42 (#97 Q4, accepted pending the A31 check): in Editor/Development builds every swing logs
    /// the phone pitch and the camera → mob distance as one greppable line, so the A31 run shows whether hits drop at
    /// steeper pitches (then d_stop is reduced later; no change now). Format (invariant culture, one line):
    /// <c>[ArReach] swing pitch=23.4 deg dist=1.32 m</c> — pitch = camera pitch in degrees, positive = phone tilted DOWN
    /// (camera looking below the horizon), negative = up; dist = horizontal camera → mob root distance in metres,
    /// <c>dist=n/a</c> without a mob. No UnityEngine dependency.
    /// </summary>
    public static class ArReachLog
    {
        public const string Prefix = "[ArReach] swing ";

        /// <summary>Pitch (degrees, + = down) of a camera whose normalized forward vector has the vertical component <paramref name="forwardY"/>.</summary>
        public static double PitchDegrees(double forwardY)
        {
            if (double.IsNaN(forwardY)) return double.NaN;
            double y = Math.Max(-1.0, Math.Min(1.0, forwardY));
            return -Math.Asin(y) * 180.0 / Math.PI;
        }

        public static string Swing(double pitchDegrees, double distanceMeters)
        {
            string pitch = double.IsNaN(pitchDegrees) || double.IsInfinity(pitchDegrees)
                ? "n/a"
                : pitchDegrees.ToString("F1", CultureInfo.InvariantCulture);
            string dist = double.IsNaN(distanceMeters) || double.IsInfinity(distanceMeters) || distanceMeters < 0.0
                ? "n/a"
                : distanceMeters.ToString("F2", CultureInfo.InvariantCulture);
            return Prefix + "pitch=" + pitch + " deg dist=" + dist + (dist == "n/a" ? string.Empty : " m");
        }
    }
}
