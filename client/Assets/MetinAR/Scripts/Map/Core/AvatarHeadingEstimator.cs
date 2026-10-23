using System;

namespace MetinAR.Map
{
    /// <summary>Where the current avatar heading came from.</summary>
    public enum HeadingSource
    {
        None = 0,
        /// <summary>Course over ground from consecutive GPS fixes (walking).</summary>
        Course = 1,
        /// <summary>Device compass (standing still, compass available).</summary>
        Compass = 2,
        /// <summary>Last known heading kept (standing still, no compass).</summary>
        Held = 3
    }

    /// <summary>Prototype tunables for <see cref="AvatarHeadingEstimator"/> (METINAR-042 #87).</summary>
    public sealed class AvatarHeadingSettings
    {
        /// <summary>Minimum displacement (m) between course samples, before accuracy scaling.</summary>
        public double MinCourseDistanceMeters = 4.0;

        /// <summary>The displacement must also exceed accuracy × this factor, so jitter does not spin the avatar.</summary>
        public double CourseAccuracyFactor = 0.8;

        /// <summary>Implied speeds below this (m/s) are not treated as walking.</summary>
        public double MinCourseSpeedMps = 0.4;

        /// <summary>Implied speeds above this (m/s) are GPS jumps, not a course.</summary>
        public double MaxCourseSpeedMps = 8.0;

        /// <summary>A course older than this (s) yields to the compass.</summary>
        public double CourseHoldSeconds = 4.0;

        /// <summary>Maximum visual turn rate (deg/s).</summary>
        public double MaxTurnDegreesPerSecond = 180.0;

        /// <summary>Exponential approach time constant (s) of the visual heading.</summary>
        public double TimeConstantSeconds = 0.35;
    }

    /// <summary>
    /// METINAR-042 (#87): engine-free heading for the map avatar ("wskaźnik kierunku poruszania", spec §2A).
    /// While walking the heading is the GPS course between accepted anchor points (jitter- and jump-filtered);
    /// when standing still it uses the compass if available, otherwise holds the last heading. The visual
    /// heading turns smoothly along the shortest arc. 0° = north, clockwise; inputs are local east/north metres.
    /// </summary>
    public sealed class AvatarHeadingEstimator
    {
        private readonly AvatarHeadingSettings _s;
        private bool _hasAnchor;
        private double _anchorE, _anchorN, _anchorT;
        private double _lastCourseT = double.NegativeInfinity;
        private bool _hasTarget;

        public AvatarHeadingEstimator(AvatarHeadingSettings settings = null)
        {
            _s = settings ?? new AvatarHeadingSettings();
        }

        public bool HasHeading => _hasTarget;

        /// <summary>Target heading in degrees [0, 360).</summary>
        public double TargetDegrees { get; private set; }

        /// <summary>Smoothed heading for the avatar visual, degrees [0, 360).</summary>
        public double VisualDegrees { get; private set; }

        public HeadingSource Source { get; private set; } = HeadingSource.None;

        /// <summary>Feeds a raw fix (local metres). Returns true when it produced a new course heading.</summary>
        public bool OnFix(double east, double north, double accuracyMeters, double timestampSeconds)
        {
            if (!Finite(east) || !Finite(north) || !Finite(timestampSeconds))
            {
                return false;
            }

            double acc = Finite(accuracyMeters) && accuracyMeters > 0 ? accuracyMeters : 0;
            if (!_hasAnchor || timestampSeconds < _anchorT)
            {
                SetAnchor(east, north, timestampSeconds);
                return false;
            }

            double dt = timestampSeconds - _anchorT;
            if (dt <= 0)
            {
                return false;
            }

            double de = east - _anchorE, dn = north - _anchorN;
            double d = Math.Sqrt(de * de + dn * dn);
            double needed = Math.Max(_s.MinCourseDistanceMeters, acc * _s.CourseAccuracyFactor);
            if (d < needed)
            {
                return false; // jitter or standing still: keep the anchor, wait for real movement
            }

            double speed = d / dt;
            if (speed > _s.MaxCourseSpeedMps)
            {
                SetAnchor(east, north, timestampSeconds); // jump / relock: restart from here, no course
                return false;
            }

            SetAnchor(east, north, timestampSeconds);
            if (speed < _s.MinCourseSpeedMps)
            {
                return false; // slow drift over a long time is not walking
            }

            SetTarget(Normalize(Math.Atan2(de, dn) * 180.0 / Math.PI), HeadingSource.Course);
            _lastCourseT = timestampSeconds;
            return true;
        }

        /// <summary>
        /// Compass reading (true heading, degrees) or null when unavailable. Used only when there is no
        /// recent course, i.e. the player is standing still.
        /// </summary>
        public void OnCompass(double? headingDegrees, double nowSeconds)
        {
            bool courseFresh = nowSeconds - _lastCourseT <= _s.CourseHoldSeconds;
            if (courseFresh)
            {
                return;
            }

            if (headingDegrees.HasValue && Finite(headingDegrees.Value))
            {
                SetTarget(Normalize(headingDegrees.Value), HeadingSource.Compass);
            }
            else if (_hasTarget)
            {
                Source = HeadingSource.Held;
            }
        }

        /// <summary>Advances the visual heading; returns the absolute turn this step (deg).</summary>
        public double Step(double deltaSeconds)
        {
            if (!_hasTarget || !(deltaSeconds > 0))
            {
                return 0;
            }

            double diff = ShortestDelta(VisualDegrees, TargetDegrees);
            double alpha = _s.TimeConstantSeconds > 0 ? 1 - Math.Exp(-deltaSeconds / _s.TimeConstantSeconds) : 1;
            double turn = diff * alpha;
            double maxTurn = _s.MaxTurnDegreesPerSecond * deltaSeconds;
            if (Math.Abs(turn) > maxTurn)
            {
                turn = Math.Sign(turn) * maxTurn;
            }

            VisualDegrees = Normalize(VisualDegrees + turn);
            return Math.Abs(turn);
        }

        /// <summary>Signed shortest rotation from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        public static double ShortestDelta(double from, double to)
        {
            double d = Normalize(to - from);
            return d > 180 ? d - 360 : d;
        }

        public static double Normalize(double degrees)
        {
            double r = degrees % 360.0;
            return r < 0 ? r + 360.0 : r;
        }

        private void SetAnchor(double e, double n, double t)
        {
            _hasAnchor = true;
            _anchorE = e;
            _anchorN = n;
            _anchorT = t;
        }

        private void SetTarget(double degrees, HeadingSource source)
        {
            if (!_hasTarget)
            {
                VisualDegrees = degrees; // first heading snaps
            }

            _hasTarget = true;
            TargetDegrees = degrees;
            Source = source;
        }

        private static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
