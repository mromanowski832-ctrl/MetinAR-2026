using System;

namespace MetinAR.ArEncounter
{
    /// <summary>What the placed AR mob is doing on its own (#97).</summary>
    public enum ArMobMotion
    {
        /// <summary>At the stand-off distance (within hysteresis), not moving.</summary>
        Holding,

        /// <summary>Walking towards the camera to the stand-off distance.</summary>
        Approaching,

        /// <summary>Backing away from a camera that came closer than d_min, to the stand-off distance.</summary>
        Retreating,

        /// <summary>Frozen: tracking lost, relocation pending, mob defeated or approach disabled.</summary>
        Paused,
    }

    /// <summary>Result of one <see cref="ArMobApproach.Tick"/>: the mob's new horizontal pose (y untouched).</summary>
    public readonly struct ArMobStep
    {
        public ArMobStep(double x, double z, double yawDegrees, double stepMeters, double distanceMeters, ArMobMotion motion, double speed)
        {
            X = x;
            Z = z;
            YawDegrees = yawDegrees;
            StepMeters = stepMeters;
            DistanceMeters = distanceMeters;
            Motion = motion;
            Speed = speed;
        }

        public double X { get; }
        public double Z { get; }
        public double YawDegrees { get; }

        /// <summary>Horizontal distance the mob moved this frame (≤ <see cref="ArNoWalkSettings.MaxStepMeters"/>).</summary>
        public double StepMeters { get; }

        /// <summary>Horizontal camera → mob distance after the step.</summary>
        public double DistanceMeters { get; }

        public ArMobMotion Motion { get; }

        /// <summary>Current speed magnitude (m/s).</summary>
        public double Speed { get; }
    }

    /// <summary>
    /// #97 rules 2–6: the mob closes the distance itself while the camera stays put. Moves only along the horizontal line
    /// to the camera, accelerates and brakes smoothly (v ≤ √(2·a·remaining)), never overshoots the stand-off distance and
    /// never moves more than v_max × min(dt, maxStepSeconds) in one frame (no teleport). Starts approaching beyond
    /// d_stop + hysteresis, retreats (animated) when the camera is closer than d_min. Yaw turns towards the camera at a
    /// limited rate. Unity convention: yaw 0 faces +Z, yaw 90 faces +X. No UnityEngine dependency.
    /// </summary>
    public sealed class ArMobApproach
    {
        private const double SnapMeters = 0.005;

        private readonly ArNoWalkSettings _settings;

        public ArMobApproach(ArNoWalkSettings settings, double stopDistanceMeters)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            if (!(stopDistanceMeters >= settings.minDistanceMeters) || double.IsInfinity(stopDistanceMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(stopDistanceMeters), "must be finite and >= minDistanceMeters");
            }

            StopDistanceMeters = stopDistanceMeters;
        }

        public ArNoWalkSettings Settings => _settings;

        /// <summary>d_stop (metres, horizontal camera → mob root).</summary>
        public double StopDistanceMeters { get; }

        /// <summary>Distance beyond which a holding mob starts to approach (d_stop + hysteresis).</summary>
        public double ApproachStartMeters => StopDistanceMeters + _settings.approachHysteresisMeters;

        public ArMobMotion Motion { get; private set; } = ArMobMotion.Holding;

        /// <summary>Current speed magnitude (m/s).</summary>
        public double Speed { get; private set; }

        /// <summary>Distance moved since construction (drives the walk-cycle phase).</summary>
        public double TravelledMeters { get; private set; }

        /// <summary>Drops any speed (e.g. after a restore) and holds until the next tick decides again.</summary>
        public void Stop()
        {
            Speed = 0.0;
            Motion = ArMobMotion.Holding;
        }

        /// <summary>
        /// One frame. <paramref name="canMove"/> false (tracking lost, relocation pending, mob defeated, approach disabled)
        /// freezes the mob in place with zero speed.
        /// </summary>
        public ArMobStep Tick(double cameraX, double cameraZ, double mobX, double mobZ, double mobYawDegrees,
                              double deltaSeconds, bool canMove)
        {
            double dx = cameraX - mobX;
            double dz = cameraZ - mobZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            bool finite = IsFinite(distance) && IsFinite(mobYawDegrees);
            double h = deltaSeconds > 0.0 && IsFinite(deltaSeconds) ? Math.Min(deltaSeconds, _settings.maxStepSeconds) : 0.0;
            if (!canMove || !finite)
            {
                Speed = 0.0;
                Motion = ArMobMotion.Paused;
                return new ArMobStep(mobX, mobZ, mobYawDegrees, 0.0, distance, Motion, 0.0);
            }

            double yaw = TurnTowards(mobYawDegrees, dx, dz, distance, h);
            if (Motion == ArMobMotion.Paused)
            {
                Motion = ArMobMotion.Holding;
            }

            if (distance < _settings.minDistanceMeters)
            {
                Motion = ArMobMotion.Retreating;
            }
            else if (Motion == ArMobMotion.Holding && distance > ApproachStartMeters)
            {
                Motion = ArMobMotion.Approaching;
            }
            else if (Motion == ArMobMotion.Retreating && distance > StopDistanceMeters)
            {
                // The camera backed off while the mob retreated: stop retreating, a later tick may approach again.
                Motion = ArMobMotion.Holding;
            }
            else if (Motion == ArMobMotion.Approaching && distance < StopDistanceMeters)
            {
                Motion = ArMobMotion.Holding;
            }

            if (Motion == ArMobMotion.Holding || distance < 1e-9 || h <= 0.0)
            {
                Speed = Motion == ArMobMotion.Holding ? 0.0 : Speed;
                return new ArMobStep(mobX, mobZ, yaw, 0.0, distance, Motion, Speed);
            }

            double remaining = Math.Abs(distance - StopDistanceMeters);
            double accel = _settings.AccelerationMetersPerSecond2;
            double brake = Math.Sqrt(2.0 * accel * remaining);
            double speed = Math.Min(Math.Min(Speed + accel * h, _settings.approachSpeedMetersPerSecond), brake);
            speed = Math.Min(speed, _settings.maxSpeedMetersPerSecond);
            double step = Math.Min(speed * h, _settings.maxSpeedMetersPerSecond * h);
            bool arrived = remaining <= step || remaining <= SnapMeters;
            if (arrived)
            {
                step = Math.Min(remaining, _settings.maxSpeedMetersPerSecond * h);
                arrived = step >= remaining - 1e-12;
            }

            // Unit vector from the mob towards the camera; approaching moves along it, retreating against it.
            double ux = dx / distance;
            double uz = dz / distance;
            double sign = Motion == ArMobMotion.Approaching ? 1.0 : -1.0;
            double nx = mobX + ux * step * sign;
            double nz = mobZ + uz * step * sign;
            TravelledMeters += step;
            double newDistance = Math.Max(0.0, distance - step * sign);
            if (arrived)
            {
                Speed = 0.0;
                Motion = ArMobMotion.Holding;
            }
            else
            {
                Speed = speed;
            }

            return new ArMobStep(nx, nz, yaw, step, newDistance, Motion, Speed);
        }

        /// <summary>Yaw (degrees, Unity convention) that faces from the mob towards (dx, dz).</summary>
        public static double YawTowards(double dx, double dz)
        {
            return Math.Atan2(dx, dz) * 180.0 / Math.PI;
        }

        /// <summary>Signed smallest angle a − b in (−180, 180].</summary>
        public static double DeltaAngle(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180.0) d -= 360.0;
            if (d <= -180.0) d += 360.0;
            return d;
        }

        private double TurnTowards(double yaw, double dx, double dz, double distance, double h)
        {
            if (distance < 1e-6 || h <= 0.0)
            {
                return yaw;
            }

            double target = YawTowards(dx, dz);
            double delta = DeltaAngle(target, yaw);
            double maxTurn = _settings.turnDegreesPerSecond * h;
            return yaw + Math.Max(-maxTurn, Math.Min(maxTurn, delta));
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
