using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// #97 P0 (owner decisions 2026-10-23 04:05/04:10): AR combat without walking. The player never walks, approaches or
    /// backs off in the AR view; the mob comes to the player on its own, smoothly, and stops at a stand-off distance
    /// derived from the same blade geometry that resolves hits. All values are PROTOTYPE (issue #97 table); gameplay stats
    /// (HP, level, damage) are not touched. No UnityEngine dependency (Unity serializes the public fields).
    /// </summary>
    [Serializable]
    public sealed class ArNoWalkSettings
    {
        /// <summary>m: margin subtracted from the blade's horizontal reach so the stand-off is safely inside reach (PROTOTYPE 0.20 m).</summary>
        public double reachMarginMeters = 0.20;

        /// <summary>d_min: the mob is never closer than this (horizontal, camera → mob root). Closer → animated retreat to d_stop (PROTOTYPE 1.10 m).</summary>
        public double minDistanceMeters = 1.10;

        /// <summary>Hysteresis above d_stop before the mob starts to approach again (PROTOTYPE 0.15 m → approach from 1.47 m).</summary>
        public double approachHysteresisMeters = 0.15;

        /// <summary>Stop tolerance around d_stop (PROTOTYPE ±0.05 m).</summary>
        public double stopToleranceMeters = 0.05;

        /// <summary>Cruise speed of the approach/retreat (PROTOTYPE 0.6 m/s).</summary>
        public double approachSpeedMetersPerSecond = 0.6;

        /// <summary>v_max: hard cap of the mob's own speed, also the no-teleport step limit (PROTOTYPE 0.8 m/s).</summary>
        public double maxSpeedMetersPerSecond = 0.8;

        /// <summary>Time to reach cruise speed from rest, and to brake from cruise to rest (PROTOTYPE 0.2 s).</summary>
        public double accelerationSeconds = 0.2;

        /// <summary>Turn rate towards the camera (PROTOTYPE 180°/s).</summary>
        public double turnDegreesPerSecond = 180.0;

        /// <summary>Longest frame time integrated in one step (PROTOTYPE 0.05 s): a frame hitch never becomes a jump.</summary>
        public double maxStepSeconds = 0.05;

        /// <summary>Farthest distance a restored mob is put at after a tracking reset (= v0 placement maximum 3.0 m).</summary>
        public double maxRestoreDistanceMeters = ArEncounterFraming.MaxPlacementDistanceMeters;

        /// <summary>
        /// Uniform visual scale of the spawned mob (lead decision 2026-10-23 04:40: 0.9; body in frame, legs may pass the
        /// screen edges; the owner may later ask for ~0.6). Also scales the hit column depth f in d_stop (<see cref="ArMobStandoff"/>).
        /// </summary>
        public double mobVisualScale = ArEncounterFraming.DefaultMobVisualScale;

        /// <summary>
        /// Lead decision 2026-10-23 04:42 (#97 Q2): after a restore the mob is put at the recorded camera drop at once (no
        /// waiting for a plane). If a horizontal plane later shows up under it within ± this tolerance of that height, the
        /// mob eases onto it within the per-frame step limit (PROTOTYPE 0.15 m).
        /// </summary>
        public double planeSettleToleranceMeters = 0.15;

        /// <summary>
        /// Lead decision 2026-10-23 04:42 (#97 Q2, "combat resumes only at stable Tracking"): after any frame without
        /// tracking, swings are allowed again only after this many seconds of uninterrupted Tracking (PROTOTYPE 0.5 s).
        /// </summary>
        public double combatResumeStableSeconds = 0.5;

        /// <summary>Acceleration (m/s²) implied by <see cref="approachSpeedMetersPerSecond"/> / <see cref="accelerationSeconds"/>.</summary>
        public double AccelerationMetersPerSecond2 => approachSpeedMetersPerSecond / accelerationSeconds;

        /// <summary>No-teleport bound: the largest distance the mob may move by itself in one frame (0.8 × 0.05 = 0.04 m).</summary>
        public double MaxStepMeters => maxSpeedMetersPerSecond * maxStepSeconds;

        /// <summary>
        /// Largest distance the mob may move by itself in a frame of <paramref name="deltaSeconds"/> (v_max × min(dt, maxStepSeconds),
        /// 0 for an invalid dt). Lead decision 04:42 (#97 Q5): every move of a VISIBLE mob stays within this; only a hidden /
        /// tracking-paused mob may be corrected by more in one go (the camera-relative restore).
        /// </summary>
        public double StepLimitMeters(double deltaSeconds)
        {
            if (!(deltaSeconds > 0.0) || double.IsInfinity(deltaSeconds)) return 0.0;
            return maxSpeedMetersPerSecond * Math.Min(deltaSeconds, maxStepSeconds);
        }

        /// <summary>Throws when a value is out of its sane range (finite, positive, ordered).</summary>
        public void Validate()
        {
            Positive(minDistanceMeters, nameof(minDistanceMeters));
            NonNegative(reachMarginMeters, nameof(reachMarginMeters));
            NonNegative(approachHysteresisMeters, nameof(approachHysteresisMeters));
            NonNegative(stopToleranceMeters, nameof(stopToleranceMeters));
            Positive(approachSpeedMetersPerSecond, nameof(approachSpeedMetersPerSecond));
            Positive(maxSpeedMetersPerSecond, nameof(maxSpeedMetersPerSecond));
            Positive(accelerationSeconds, nameof(accelerationSeconds));
            Positive(turnDegreesPerSecond, nameof(turnDegreesPerSecond));
            Positive(maxStepSeconds, nameof(maxStepSeconds));
            Positive(maxRestoreDistanceMeters, nameof(maxRestoreDistanceMeters));
            Positive(mobVisualScale, nameof(mobVisualScale));
            NonNegative(planeSettleToleranceMeters, nameof(planeSettleToleranceMeters));
            NonNegative(combatResumeStableSeconds, nameof(combatResumeStableSeconds));
            if (approachSpeedMetersPerSecond > maxSpeedMetersPerSecond)
            {
                throw new ArgumentOutOfRangeException(nameof(approachSpeedMetersPerSecond), "must be <= maxSpeedMetersPerSecond");
            }

            if (maxRestoreDistanceMeters < minDistanceMeters)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRestoreDistanceMeters), "must be >= minDistanceMeters");
            }
        }

        private static void Positive(double v, string name)
        {
            if (!(v > 0.0) || double.IsInfinity(v))
            {
                throw new ArgumentOutOfRangeException(name, "must be finite and > 0");
            }
        }

        private static void NonNegative(double v, string name)
        {
            if (!(v >= 0.0) || double.IsInfinity(v))
            {
                throw new ArgumentOutOfRangeException(name, "must be finite and >= 0");
            }
        }
    }

    /// <summary>
    /// Lead decision 2026-10-23 04:42 (#97 Q9): the mob approach can be switched off ONLY through the editor test seam
    /// (legacy smoke tests that hold the mob still). In a game build the request is ignored and the approach stays on.
    /// </summary>
    public static class ArMobApproachGate
    {
        /// <summary>Effective approach flag: a "disable" request counts only when the test seam is available.</summary>
        public static bool Effective(bool requestedEnabled, bool testSeamAvailable)
        {
            return requestedEnabled || !testSeamAvailable;
        }
    }

    /// <summary>
    /// #97 rule 8: the stand-off distance comes from the blade geometry that resolves hits:
    /// d_stop = R_h − m + f, with R_h the blade's horizontal reach from the camera (<c>FppCombatController.BladeReachMeters</c>),
    /// m the margin and f the depth of the hit column in front of the mob root (<see cref="FppHitGeometry.ReachColumnMax"/>.Z
    /// × the mob's visual scale). A mob at d is hittable at level aim iff d − f ≤ R_h.
    /// </summary>
    public static class ArMobStandoff
    {
        /// <summary>f: how far the hit column reaches in front of the mob root, in metres, at <paramref name="mobScale"/>.</summary>
        public static double HitFrontOffsetMeters(double mobScale)
        {
            return FppHitGeometry.ReachColumnMax.Z * mobScale;
        }

        /// <summary>Unclamped R_h − m + f.</summary>
        public static double RawStopDistance(double bladeReachMeters, double marginMeters, double frontOffsetMeters)
        {
            return bladeReachMeters - marginMeters + frontOffsetMeters;
        }

        /// <summary>d_stop from the configured <see cref="ArNoWalkSettings.mobVisualScale"/> (the scale the spawned mob uses).</summary>
        public static double StopDistance(double bladeReachMeters, ArNoWalkSettings settings, out bool clamped)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return StopDistance(bladeReachMeters, settings, settings.mobVisualScale, out clamped);
        }

        /// <summary>d_stop, never below d_min (<paramref name="clamped"/> is set when the geometry alone would put it closer).</summary>
        public static double StopDistance(double bladeReachMeters, ArNoWalkSettings settings, double mobScale, out bool clamped)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            double raw = RawStopDistance(bladeReachMeters, settings.reachMarginMeters, HitFrontOffsetMeters(mobScale));
            clamped = !(raw >= settings.minDistanceMeters);
            return clamped ? settings.minDistanceMeters : raw;
        }
    }
}
