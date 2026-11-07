using System;
using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// #31 decision 18:58 (+ correction 19:00): in v0 the mob stands still and the player walks up to it. While the placed,
    /// visible, undefeated mob is outside attack reach the HUD shows „Podejdź – 2,4 m” (horizontal camera-to-mob distance,
    /// one decimal). Hidden in reach, after the kill (lead confirmed) and while tracking is lost („Szukam otoczenia…” has
    /// priority). Attack reach is DISTANCE ONLY, independent of where the phone aims: the horizontal distance from the camera
    /// to the mob's hit footprint versus the blade's horizontal reach, both taken from existing geometry (no new constant).
    /// The aimed blade sweep stays for hits only. Engine-free.
    /// </summary>
    public static class ArApproachPrompt
    {
        public const string Prefix = "Podejdź – ";

        /// <summary>„Podejdź – 2,4 m” (Polish decimal comma, rounded half away from zero); empty for a non-finite or negative distance.</summary>
        public static string Format(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0) return string.Empty;
            double rounded = Math.Round(meters, 1, MidpointRounding.AwayFromZero);
            return Prefix + rounded.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " m";
        }

        /// <summary>
        /// Blade's horizontal reach from the camera, independent of aim: the largest camera-local length of the sampled blade
        /// points (base/tip over the ATAK hit window). For any camera pitch/yaw the horizontal reach cannot exceed it, and at
        /// a level aim it is reached. 0 for no finite point.
        /// </summary>
        public static double BladeReach(System.Collections.Generic.IEnumerable<FppPoint> cameraLocalBladePoints)
        {
            double max = 0;
            if (cameraLocalBladePoints == null) return max;
            foreach (FppPoint p in cameraLocalBladePoints)
            {
                if (!p.IsFinite) continue;
                max = Math.Max(max, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z));
            }

            return max;
        }

        /// <summary>
        /// Horizontal distance (mob-local x/z) from a point to the footprint rectangle [minX,maxX]×[minZ,maxZ]; 0 inside.
        /// NaN for non-finite input.
        /// </summary>
        public static double DistanceToFootprint(double x, double z, double minX, double maxX, double minZ, double maxZ)
        {
            if (double.IsNaN(x) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(z)) return double.NaN;
            double dx = x < minX ? minX - x : x > maxX ? x - maxX : 0;
            double dz = z < minZ ? minZ - z : z > maxZ ? z - maxZ : 0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Out of attack reach: the camera is farther from the hit footprint than the blade reaches (distance only).</summary>
        public static bool IsOutOfReach(double distanceToFootprintMeters, double bladeReachMeters)
        {
            if (double.IsNaN(distanceToFootprintMeters) || double.IsNaN(bladeReachMeters)) return false;
            return distanceToFootprintMeters > bladeReachMeters;
        }

        /// <summary>True only for a placed, usable (tracking, anchored) and undefeated mob that is out of attack reach.</summary>
        public static bool ShouldShow(bool placed, bool mobUsable, bool mobDefeated, bool inAttackReach, double meters)
        {
            return placed && mobUsable && !mobDefeated && !inAttackReach
                   && !double.IsNaN(meters) && !double.IsInfinity(meters) && meters >= 0;
        }
    }
}
