using System;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Technical prototype thresholds for <see cref="AggroConfidenceGate"/>.
    /// These are GPS-noise filtering values for the prototype, not final game-design balance.
    /// Pure C# — no UnityEngine dependency; serializable so a mob can expose it in the Inspector.
    /// </summary>
    [Serializable]
    public sealed class AggroGateSettings
    {
        /// <summary>
        /// Fixes whose horizontal accuracy radius is larger than this (meters) are ignored and
        /// break the stability streak. Prototype value: 10 m.
        /// </summary>
        public double maxHorizontalAccuracyMeters = 10.0;

        /// <summary>
        /// Multiplier applied to the fix accuracy radius. A fix is confidently inside only when
        /// distance + accuracy * factor &lt;= aggro range (the uncertainty circle lies inside the range).
        /// Platform accuracy is roughly a 68% radius; 1.6x approximates a 95% 2D radius.
        /// Prototype value: 1.6.
        /// </summary>
        public double accuracyMarginFactor = 1.6;

        /// <summary>
        /// Consecutive confidently-inside fixes (distinct timestamps) needed to enter aggro. Prototype value: 3.
        /// </summary>
        public int requiredStableFixes = 3;

        /// <summary>
        /// Minimum time (seconds, fix timestamps) between the first and last fix of the inside streak.
        /// Prototype value: 2 s.
        /// </summary>
        public double minStableSeconds = 2.0;

        /// <summary>
        /// A gap between consecutive fixes larger than this (seconds) resets the stability history,
        /// e.g. after pause/resume. Prototype value: 10 s.
        /// </summary>
        public double maxFixGapSeconds = 10.0;

        /// <summary>
        /// After an attack, the mob re-arms only when the player is confidently beyond
        /// aggro range + this distance: distance - accuracy * factor &gt;= range + extra. Prototype value: 6 m.
        /// </summary>
        public double rearmExtraMeters = 6.0;

        /// <summary>
        /// Consecutive confidently-far fixes needed to re-arm. Prototype value: 2.
        /// </summary>
        public int requiredRearmFixes = 2;

        /// <summary>
        /// Minimum fix-time between two attacks even after re-arming (replaces the former 8 s debounce).
        /// Prototype value: 8 s.
        /// </summary>
        public double minRetriggerSeconds = 8.0;

        public static AggroGateSettings CreatePrototypeDefaults()
        {
            return new AggroGateSettings();
        }
    }

    /// <summary>
    /// Decides when an aggressive mob may enter aggro from noisy GPS fixes.
    /// Raw distance alone is never enough: the fix must be accurate enough, its accuracy circle must
    /// lie inside the aggro range, and that must hold for a short stable streak of distinct fixes.
    /// After triggering, the gate stays disengaged (hysteresis) until the player is confidently far away.
    /// Pure C# — no UnityEngine dependency; deterministic for tests.
    /// </summary>
    public sealed class AggroConfidenceGate
    {
        private readonly AggroGateSettings _settings;

        private bool _hasLastTimestamp;
        private double _lastTimestamp;
        private int _insideCount;
        private double _insideStartTimestamp;
        private int _outsideCount;
        private bool _hasTriggered;
        private double _lastTriggerTimestamp;

        public AggroConfidenceGate(AggroGateSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            IsArmed = true;
        }

        public AggroGateSettings Settings => _settings;

        /// <summary>True when the next confident approach may trigger aggro.</summary>
        public bool IsArmed { get; private set; }

        /// <summary>Current consecutive confidently-inside fix count.</summary>
        public int StableInsideCount => _insideCount;

        /// <summary>Current consecutive confidently-far fix count (while disengaged).</summary>
        public int StableOutsideCount => _outsideCount;

        /// <summary>Number of times <see cref="Submit"/> returned true.</summary>
        public int TriggerCount { get; private set; }

        /// <summary>
        /// Feeds one location fix. Repeated calls with the same timestamp are ignored, so it is safe to
        /// call every frame with the latest fix.
        /// </summary>
        /// <param name="distanceMeters">Estimated horizontal distance player → mob.</param>
        /// <param name="horizontalAccuracyMeters">Fix accuracy radius in meters.</param>
        /// <param name="timestampSeconds">Fix timestamp in seconds.</param>
        /// <param name="aggroRangeMeters">Mob aggro range in meters.</param>
        /// <returns>True exactly when aggro should be entered now.</returns>
        public bool Submit(
            double distanceMeters,
            double horizontalAccuracyMeters,
            double timestampSeconds,
            double aggroRangeMeters)
        {
            if (!IsFinite(distanceMeters) || !IsFinite(horizontalAccuracyMeters) || !IsFinite(timestampSeconds)
                || !IsFinite(aggroRangeMeters) || aggroRangeMeters <= 0.0
                || distanceMeters < 0.0 || horizontalAccuracyMeters < 0.0)
            {
                return false;
            }

            if (_hasLastTimestamp)
            {
                if (timestampSeconds == _lastTimestamp)
                {
                    return false; // same fix seen again (e.g. every frame)
                }

                if (timestampSeconds < _lastTimestamp
                    || timestampSeconds - _lastTimestamp > _settings.maxFixGapSeconds)
                {
                    ClearHistory(); // new stream or stale history
                    if (timestampSeconds < _lastTimestamp)
                    {
                        _hasTriggered = false; // timestamps restarted; cooldown reference is meaningless
                    }
                }
            }

            _hasLastTimestamp = true;
            _lastTimestamp = timestampSeconds;

            if (horizontalAccuracyMeters > _settings.maxHorizontalAccuracyMeters)
            {
                ClearHistory();
                return false;
            }

            double margin = horizontalAccuracyMeters * _settings.accuracyMarginFactor;

            if (!IsArmed)
            {
                if (distanceMeters - margin >= aggroRangeMeters + _settings.rearmExtraMeters)
                {
                    _outsideCount++;
                    if (_outsideCount >= Math.Max(1, _settings.requiredRearmFixes))
                    {
                        IsArmed = true;
                        ClearHistory();
                    }
                }
                else
                {
                    _outsideCount = 0;
                }

                return false;
            }

            if (distanceMeters + margin > aggroRangeMeters)
            {
                _insideCount = 0;
                return false;
            }

            if (_insideCount == 0)
            {
                _insideStartTimestamp = timestampSeconds;
            }

            _insideCount++;

            bool stable = _insideCount >= Math.Max(1, _settings.requiredStableFixes)
                          && timestampSeconds - _insideStartTimestamp >= _settings.minStableSeconds;
            bool cooledDown = !_hasTriggered
                              || timestampSeconds - _lastTriggerTimestamp >= _settings.minRetriggerSeconds;
            if (!stable || !cooledDown)
            {
                return false;
            }

            IsArmed = false;
            ClearHistory();
            _hasTriggered = true;
            _lastTriggerTimestamp = timestampSeconds;
            TriggerCount++;
            return true;
        }

        /// <summary>Re-arms and forgets all history (e.g. when the mob is respawned).</summary>
        public void Reset()
        {
            IsArmed = true;
            ClearHistory();
            _hasLastTimestamp = false;
            _hasTriggered = false;
        }

        private void ClearHistory()
        {
            _insideCount = 0;
            _outsideCount = 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
