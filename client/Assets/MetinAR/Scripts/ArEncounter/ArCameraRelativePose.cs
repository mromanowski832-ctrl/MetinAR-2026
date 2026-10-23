using System;

namespace MetinAR.ArEncounter
{
    /// <summary>A world pose of the AR camera or mob: position (metres) and yaw (degrees, Unity: 0 = +Z, 90 = +X).</summary>
    public readonly struct ArYawPose
    {
        public ArYawPose(double x, double y, double z, double yawDegrees)
        {
            X = x;
            Y = y;
            Z = z;
            YawDegrees = yawDegrees;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double YawDegrees { get; }

        public static double HorizontalDistance(ArYawPose a, ArYawPose b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// #97 rule 7: the mob's pose relative to the camera, recorded every usable frame so that after a tracking reset the
    /// mob comes back at the same horizontal distance and bearing relative to the camera's heading, the same height below
    /// the camera and the same facing relative to the camera — instead of being re-placed at the reticle. The restore
    /// distance is clamped to [d_min, max restore distance]. No UnityEngine dependency.
    /// </summary>
    public readonly struct ArCameraRelativePose
    {
        public ArCameraRelativePose(double distanceMeters, double bearingDegrees, double dropBelowCameraMeters, double yawOffsetDegrees)
        {
            DistanceMeters = distanceMeters;
            BearingDegrees = bearingDegrees;
            DropBelowCameraMeters = dropBelowCameraMeters;
            YawOffsetDegrees = yawOffsetDegrees;
            IsValid = true;
        }

        /// <summary>Horizontal camera → mob distance.</summary>
        public double DistanceMeters { get; }

        /// <summary>Direction camera → mob relative to the camera's yaw (0 = straight ahead, + = to the right).</summary>
        public double BearingDegrees { get; }

        /// <summary>Camera height above the mob root.</summary>
        public double DropBelowCameraMeters { get; }

        /// <summary>Mob yaw relative to the camera's yaw.</summary>
        public double YawOffsetDegrees { get; }

        public bool IsValid { get; }

        public static ArCameraRelativePose Capture(ArYawPose camera, ArYawPose mob)
        {
            double dx = mob.X - camera.X;
            double dz = mob.Z - camera.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double bearing = distance < 1e-9 ? 0.0 : ArMobApproach.DeltaAngle(ArMobApproach.YawTowards(dx, dz), camera.YawDegrees);
            return new ArCameraRelativePose(distance, bearing, camera.Y - mob.Y,
                                            ArMobApproach.DeltaAngle(mob.YawDegrees, camera.YawDegrees));
        }

        /// <summary>The restored distance: the recorded one clamped to [<paramref name="minMeters"/>, <paramref name="maxMeters"/>].</summary>
        public double ClampedDistance(double minMeters, double maxMeters)
        {
            return Math.Max(minMeters, Math.Min(maxMeters, DistanceMeters));
        }

        /// <summary>The mob's world pose for <paramref name="camera"/> (same relative pose, distance clamped).</summary>
        public ArYawPose Restore(ArYawPose camera, double minMeters, double maxMeters)
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("no recorded camera-relative pose");
            }

            double d = ClampedDistance(minMeters, maxMeters);
            double heading = (camera.YawDegrees + BearingDegrees) * Math.PI / 180.0;
            return new ArYawPose(camera.X + Math.Sin(heading) * d, camera.Y - DropBelowCameraMeters, camera.Z + Math.Cos(heading) * d,
                                 camera.YawDegrees + YawOffsetDegrees);
        }
    }
}
