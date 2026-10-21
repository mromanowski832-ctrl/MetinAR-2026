using System;
using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Engine-free probe for the v0 acceptance bug "the spider moves together with the phone" (#31, A31 retest
    /// 2026-10-21). It records the camera and monster poses at placement and then reports, per sample, how the player
    /// moved relative to the monster (rotation, sidestep, closer / farther), how far the monster drifted in world space,
    /// and a verdict: <c>FOLLOWS_CAMERA</c> when the monster drifted with the camera instead of staying at its spot.
    /// Only diagnostics: it never moves anything.
    /// </summary>
    public sealed class ArAnchorProbe
    {
        /// <summary>Monster drift (m) above which it is considered to have left its world spot.</summary>
        public const double DriftToleranceMeters = 0.10;

        /// <summary>Camera horizontal displacement (m) below which a drift cannot be attributed to the player walking.</summary>
        public const double MinCameraMoveMeters = 0.20;

        private double _camX0, _camZ0, _camYaw0, _mobX0, _mobY0, _mobZ0;

        public bool HasBaseline { get; private set; }

        public int Samples { get; private set; }

        /// <summary>Largest world-space drift of the monster since placement (m).</summary>
        public double MaxDriftMeters { get; private set; }

        /// <summary>Samples that looked like the monster following the camera.</summary>
        public int FollowsCameraSamples { get; private set; }

        public void SetBaseline(double camX, double camZ, double camYawDegrees, double mobX, double mobY, double mobZ)
        {
            _camX0 = camX; _camZ0 = camZ; _camYaw0 = camYawDegrees;
            _mobX0 = mobX; _mobY0 = mobY; _mobZ0 = mobZ;
            HasBaseline = true;
            Samples = 0;
            MaxDriftMeters = 0;
            FollowsCameraSamples = 0;
        }

        public ArAnchorSample Sample(double camX, double camZ, double camYawDegrees, double mobX, double mobY, double mobZ)
        {
            if (!HasBaseline) SetBaseline(camX, camZ, camYawDegrees, mobX, mobY, mobZ);

            var s = new ArAnchorSample();
            double dx = mobX - _mobX0, dy = mobY - _mobY0, dz = mobZ - _mobZ0;
            s.MobDrift = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double cdx = camX - _camX0, cdz = camZ - _camZ0;
            s.CameraMove = Math.Sqrt(cdx * cdx + cdz * cdz);
            s.Yaw = WrapDegrees(camYawDegrees - _camYaw0);

            // Split the player's ground movement along the original camera->monster line: radial (+ = closer)
            // and lateral (sidestep, + = to the right when facing the monster).
            double lx = _mobX0 - _camX0, lz = _mobZ0 - _camZ0;
            double len = Math.Sqrt(lx * lx + lz * lz);
            if (len > 1e-6)
            {
                lx /= len; lz /= len;
                s.Closer = cdx * lx + cdz * lz;
                s.Sidestep = cdx * lz - cdz * lx;
            }

            s.Distance = ArEncounterFraming.HorizontalDistance(camX, camZ, mobX, mobZ);
            s.DistanceAtPlacement = len;

            // Monster moved in world space, roughly by the same amount and direction as the camera.
            if (s.MobDrift > DriftToleranceMeters && s.CameraMove > MinCameraMoveMeters)
            {
                double dot = (dx * cdx + dz * cdz) / (Math.Sqrt(dx * dx + dz * dz) * s.CameraMove + 1e-9);
                s.FollowsCamera = dot > 0.7 && s.MobDrift > 0.5 * s.CameraMove;
            }

            s.Verdict = s.FollowsCamera ? "FOLLOWS_CAMERA" : s.MobDrift > DriftToleranceMeters ? "DRIFT" : "STAYS";
            Samples++;
            if (s.MobDrift > MaxDriftMeters) MaxDriftMeters = s.MobDrift;
            if (s.FollowsCamera) FollowsCameraSamples++;
            return s;
        }

        public static double WrapDegrees(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0;
            if (a <= -180.0) a += 360.0;
            return a;
        }
    }

    public struct ArAnchorSample
    {
        public double MobDrift, CameraMove, Yaw, Closer, Sidestep, Distance, DistanceAtPlacement;
        public bool FollowsCamera;
        public string Verdict;

        public override string ToString()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Format(c,
                "verdict={0} drift={1:F2} camMove={2:F2} yaw={3:F0} closer={4:F2} side={5:F2} d={6:F2} d0={7:F2}",
                Verdict, MobDrift, CameraMove, Yaw, Closer, Sidestep, Distance, DistanceAtPlacement);
        }
    }
}
