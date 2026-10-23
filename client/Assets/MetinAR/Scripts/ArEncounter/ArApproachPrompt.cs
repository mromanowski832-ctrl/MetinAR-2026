using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Attack-reach geometry of the AR encounter (#31, kept by #97). Attack reach is DISTANCE ONLY, independent of where the
    /// phone aims: the horizontal distance from the camera to the mob's hit footprint versus the blade's horizontal reach,
    /// both taken from the sword/hit geometry. #97 removed the v0 walk-up prompt („Podejdź – x,x m”, formerly
    /// <c>Prefix</c>/<c>Format</c>/<c>ShouldShow</c>): the player no longer walks in AR, the mob comes into reach by itself
    /// (<see cref="ArMobApproach"/>, stand-off from <see cref="ArMobStandoff"/>). The class name is kept to limit churn. Engine-free.
    /// </summary>
    public static class ArApproachPrompt
    {
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
    }
}
