using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Engine-free placement and nameplate framing rules of the AR encounter (#31 owner retest on A31, 2026-10-21):
    /// the spider was placed too close and filled the screen, and its world-space nameplate was huge up close.
    /// Visual/technical prototype values only: the spider's gameplay stats (level, HP, aggro) are not touched.
    /// </summary>
    public static class ArEncounterFraming
    {
        /// <summary>Minimum horizontal (ground) distance from the AR camera to the placement target.</summary>
        public const double MinPlacementDistanceMeters = 1.5;

        /// <summary>Tolerance so a target exactly at the minimum is not rejected by float noise.</summary>
        public const double PlacementDistanceTolerance = 0.01;

        /// <summary>Uniform visual scale of the spawned procedural spider (~0.93 m leg span instead of ~1.24 m).</summary>
        public const float MobVisualScale = 0.75f;

        /// <summary>Unscaled nameplate HP rim width of the procedural spider prefab (metres).</summary>
        public const double NameplateReferenceWidthMeters = 0.84;

        /// <summary>Maximum angular width of the nameplate (HP bar about a quarter, name about a third of the estimated A31 portrait width).</summary>
        public const double NameplateMaxAngleDegrees = 9.0;

        public const double NameplateMinScale = 0.05;
        public const double NameplateMaxScale = 1.0;

        /// <summary>Highest viewport Y of the nameplate anchor; above it is the top HUD band (player card, radar).</summary>
        public const double NameplateMaxViewportY = 0.80;

        /// <summary>Minimum viewport gap kept between the mob's top and the nameplate anchor.</summary>
        public const double NameplateMobGapViewport = 0.04;

        public static double HorizontalDistance(double fromX, double fromZ, double toX, double toZ)
        {
            double dx = toX - fromX, dz = toZ - fromZ;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>True when the target is at least <see cref="MinPlacementDistanceMeters"/> away along the ground.</summary>
        public static bool IsPlacementFarEnough(double cameraX, double cameraZ, double targetX, double targetZ)
        {
            double d = HorizontalDistance(cameraX, cameraZ, targetX, targetZ);
            return !double.IsNaN(d) && !double.IsInfinity(d) && d >= MinPlacementDistanceMeters - PlacementDistanceTolerance;
        }

        /// <summary>
        /// World scale of the nameplate at <paramref name="distanceMeters"/> from the camera: constant angular size
        /// (<see cref="NameplateMaxAngleDegrees"/>) when close, never larger than the authored size (scale 1).
        /// </summary>
        public static double NameplateScale(double distanceMeters)
        {
            if (double.IsNaN(distanceMeters) || double.IsInfinity(distanceMeters) || distanceMeters <= 0) return NameplateMinScale;
            double width = 2.0 * distanceMeters * Math.Tan(NameplateMaxAngleDegrees * Math.PI / 360.0);
            return Math.Max(NameplateMinScale, Math.Min(NameplateMaxScale, width / NameplateReferenceWidthMeters));
        }

        /// <summary>Angular width (degrees) of the nameplate at a distance with a given world scale.</summary>
        public static double NameplateAngleDegrees(double distanceMeters, double scale)
            => 2.0 * Math.Atan(NameplateReferenceWidthMeters * scale * 0.5 / distanceMeters) * 180.0 / Math.PI;

        /// <summary>
        /// Viewport Y for the nameplate anchor: kept below the top HUD band, but never pushed down onto the mob
        /// (stays at least <see cref="NameplateMobGapViewport"/> above the mob's top).
        /// </summary>
        public static double ClampNameplateViewportY(double labelViewportY, double mobTopViewportY)
        {
            if (double.IsNaN(labelViewportY)) return labelViewportY;
            if (labelViewportY <= NameplateMaxViewportY) return labelViewportY;
            double floor = double.IsNaN(mobTopViewportY) ? NameplateMaxViewportY : mobTopViewportY + NameplateMobGapViewport;
            return Math.Max(NameplateMaxViewportY, Math.Min(labelViewportY, floor));
        }
    }
}
