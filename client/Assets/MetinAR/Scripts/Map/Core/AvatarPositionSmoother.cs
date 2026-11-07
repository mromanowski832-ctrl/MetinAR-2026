using System;

namespace MetinAR.Map
{
    /// <summary>What <see cref="AvatarPositionSmoother.OnFix"/> did with a sample.</summary>
    public enum AvatarFixResult
    {
        /// <summary>Same timestamp as the previous sample (the provider is polled every frame): no change.</summary>
        Duplicate = 0,
        /// <summary>First fix: the visual position snaps to it.</summary>
        First = 1,
        /// <summary>New target accepted; the visual moves there smoothly.</summary>
        Accepted = 2,
        /// <summary>Movement below the jitter threshold: the target stays where it is.</summary>
        IgnoredJitter = 3,
        /// <summary>Implausibly fast jump: held until a second fix confirms it.</summary>
        HeldOutlier = 4,
        /// <summary>A held jump was confirmed by the next fix and accepted.</summary>
        ConfirmedJump = 5,
        /// <summary>Very large move (relock after signal loss, teleport): target and visual snap.</summary>
        Snapped = 6,
        /// <summary>Older timestamp than the last accepted sample: ignored.</summary>
        Stale = 7
    }

    /// <summary>Tunables (prototype values, see docs/METINAR-008B-MAP-POC.md).</summary>
    public sealed class AvatarSmoothingSettings
    {
        /// <summary>Minimum movement (m) that moves the target.</summary>
        public double JitterMinMeters = 2.0;
        /// <summary>Extra jitter threshold per metre of reported horizontal accuracy.</summary>
        public double JitterAccuracyFactor = 0.3;
        /// <summary>Upper bound of the accuracy-scaled jitter threshold (m), so walking still registers.</summary>
        public double JitterMaxMeters = 6.0;
        /// <summary>Implied speed (m/s) above which a single fix is treated as a possible outlier.</summary>
        public double MaxPlausibleSpeedMps = 8.0;
        /// <summary>Radius (m) in which the next fix confirms a held jump.</summary>
        public double ConfirmRadiusMeters = 10.0;
        /// <summary>Distance (m) at or above which target and visual snap instead of gliding.</summary>
        public double SnapDistanceMeters = 50.0;
        /// <summary>Gap (s) after which a fix is accepted without the speed check (signal relock).</summary>
        public double RelockGapSeconds = 10.0;
        /// <summary>Exponential approach time constant (s) of the visual towards the target.</summary>
        public double TimeConstantSeconds = 0.6;
        /// <summary>Maximum visual speed (m/s) while gliding.</summary>
        public double MaxVisualSpeedMps = 6.0;
        /// <summary>A held jump with no newer fix for this long (s) is accepted (it is the latest truth).</summary>
        public double PendingTimeoutSeconds = 2.0;
    }

    /// <summary>
    /// METINAR-008B: engine-free smoothing of the *visual* player marker on the map. Raw GPS fixes go in
    /// (local east/north metres relative to the map origin, accuracy, timestamp); a smoothed visual position
    /// comes out. Gameplay keeps using the raw fix. No coordinates are stored beyond local metres.
    /// </summary>
    public sealed class AvatarPositionSmoother
    {
        private readonly AvatarSmoothingSettings _s;
        private bool _hasTarget;
        private double _targetE, _targetN;
        private double _visualE, _visualN;
        private double _lastSampleTime = double.NaN;
        private double _lastAcceptedTime;
        private bool _hasPending;
        private double _pendingE, _pendingN, _pendingAge;

        public AvatarPositionSmoother(AvatarSmoothingSettings settings = null)
        {
            _s = settings ?? new AvatarSmoothingSettings();
        }

        public AvatarSmoothingSettings Settings => _s;
        public bool HasPosition => _hasTarget;
        public double TargetEast => _targetE;
        public double TargetNorth => _targetN;
        public double VisualEast => _visualE;
        public double VisualNorth => _visualN;
        public bool HasPendingOutlier => _hasPending;

        /// <summary>How many held jumps were accepted by <see cref="AvatarSmoothingSettings.PendingTimeoutSeconds"/>.</summary>
        public int PendingTimeouts { get; private set; }

        /// <summary>Distance (m) between the smoothed visual and the accepted target.</summary>
        public double LagMeters => Dist(_visualE, _visualN, _targetE, _targetN);

        /// <summary>Jitter threshold (m) for a given reported accuracy.</summary>
        public double JitterThreshold(double accuracyMeters)
        {
            double acc = double.IsNaN(accuracyMeters) || accuracyMeters < 0 ? 0 : accuracyMeters;
            return Math.Max(_s.JitterMinMeters, Math.Min(_s.JitterMaxMeters, _s.JitterAccuracyFactor * acc));
        }

        /// <summary>Feeds the latest fix. Safe to call every frame with the same fix (returns Duplicate).</summary>
        public AvatarFixResult OnFix(double east, double north, double accuracyMeters, double timestampSeconds)
        {
            if (double.IsNaN(east) || double.IsNaN(north) || double.IsInfinity(east) || double.IsInfinity(north))
            {
                return AvatarFixResult.Stale;
            }

            if (!_hasTarget)
            {
                _lastSampleTime = timestampSeconds;
                SnapTo(east, north, timestampSeconds);
                return AvatarFixResult.First;
            }

            if (timestampSeconds == _lastSampleTime)
            {
                return AvatarFixResult.Duplicate;
            }

            if (timestampSeconds < _lastSampleTime)
            {
                return AvatarFixResult.Stale;
            }

            _lastSampleTime = timestampSeconds;
            double d = Dist(east, north, _targetE, _targetN);

            if (_hasPending)
            {
                bool confirms = Dist(east, north, _pendingE, _pendingN) <= Math.Max(_s.ConfirmRadiusMeters, JitterThreshold(accuracyMeters));
                _hasPending = false;
                if (confirms)
                {
                    SetTarget(east, north, timestampSeconds);
                    if (d >= _s.SnapDistanceMeters)
                    {
                        _visualE = east;
                        _visualN = north;
                    }

                    return AvatarFixResult.ConfirmedJump;
                }
                // The held sample was an outlier; evaluate this one normally.
            }

            double gap = timestampSeconds - _lastAcceptedTime;
            if (d >= _s.SnapDistanceMeters && gap >= _s.RelockGapSeconds)
            {
                SnapTo(east, north, timestampSeconds);
                return AvatarFixResult.Snapped;
            }

            if (d < JitterThreshold(accuracyMeters))
            {
                return AvatarFixResult.IgnoredJitter;
            }

            double speed = gap > 0 ? d / gap : double.PositiveInfinity;
            if (gap < _s.RelockGapSeconds && speed > _s.MaxPlausibleSpeedMps)
            {
                _hasPending = true;
                _pendingE = east;
                _pendingN = north;
                _pendingAge = 0;
                return AvatarFixResult.HeldOutlier;
            }

            SetTarget(east, north, timestampSeconds);
            if (d >= _s.SnapDistanceMeters)
            {
                _visualE = east;
                _visualN = north;
                return AvatarFixResult.Snapped;
            }

            return AvatarFixResult.Accepted;
        }

        /// <summary>
        /// Snaps target and visual immediately (e.g. a deliberate teleport such as the demo/mock move).
        /// </summary>
        public void SnapTo(double east, double north, double timestampSeconds)
        {
            _hasTarget = true;
            _hasPending = false;
            _targetE = _visualE = east;
            _targetN = _visualN = north;
            _lastAcceptedTime = timestampSeconds;
        }

        /// <summary>Advances the visual towards the target: exponential approach with a speed cap.</summary>
        /// <returns>Distance (m) the visual moved in this step.</returns>
        public double Step(double deltaSeconds)
        {
            if (!_hasTarget || !(deltaSeconds > 0))
            {
                return 0;
            }

            if (_hasPending)
            {
                _pendingAge += deltaSeconds;
                if (_pendingAge >= _s.PendingTimeoutSeconds)
                {
                    _hasPending = false;
                    bool far = Dist(_pendingE, _pendingN, _targetE, _targetN) >= _s.SnapDistanceMeters;
                    _targetE = _pendingE;
                    _targetN = _pendingN;
                    _lastAcceptedTime = _lastSampleTime;
                    PendingTimeouts++;
                    if (far)
                    {
                        double jumped = Dist(_visualE, _visualN, _targetE, _targetN);
                        _visualE = _targetE;
                        _visualN = _targetN;
                        return jumped;
                    }
                }
            }

            double dx = _targetE - _visualE, dy = _targetN - _visualN;
            double lag = Math.Sqrt(dx * dx + dy * dy);
            if (lag < 1e-6)
            {
                _visualE = _targetE;
                _visualN = _targetN;
                return 0;
            }

            if (lag >= _s.SnapDistanceMeters)
            {
                _visualE = _targetE;
                _visualN = _targetN;
                return lag;
            }

            double tau = _s.TimeConstantSeconds > 1e-6 ? _s.TimeConstantSeconds : 1e-6;
            double move = lag * (1.0 - Math.Exp(-deltaSeconds / tau));
            double cap = _s.MaxVisualSpeedMps * deltaSeconds;
            if (move > cap) move = cap;
            if (move > lag) move = lag;
            _visualE += dx / lag * move;
            _visualN += dy / lag * move;
            return move;
        }

        private void SetTarget(double e, double n, double t)
        {
            _targetE = e;
            _targetN = n;
            _lastAcceptedTime = t;
        }

        private static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
