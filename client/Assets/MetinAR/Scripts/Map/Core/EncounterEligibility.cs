using System;

namespace MetinAR.Map
{
    /// <summary>Where the player position came from. Only <see cref="Gps"/> may earn production rewards.</summary>
    public enum LocationSourceKind
    {
        Gps = 0,
        /// <summary>Developer simulation (mock provider, editor movement, replay). Rewards are tagged and never saved as production.</summary>
        Simulation = 1
    }

    /// <summary>Whether the "Walcz" (start AR encounter) action is allowed right now.</summary>
    public enum EncounterAvailability
    {
        /// <summary>No fix yet / mob not placed.</summary>
        None = 0,
        TooFar = 1,
        /// <summary>The fix accuracy radius is too large to be sure the player is in range.</summary>
        PoorAccuracy = 2,
        /// <summary>The latest fix is too old (signal lost, app resumed).</summary>
        StaleFix = 3,
        /// <summary>The encounter site failed the safety policy (should not normally reach the map).</summary>
        UnsafeSite = 4,
        Available = 5,
        /// <summary>In range, but the player is still walking. Rule: stop on the map first, then turn the camera on.</summary>
        Moving = 6
    }

    /// <summary>
    /// Prototype thresholds for <see cref="EncounterEligibility"/> (METINAR-042 #87). Technical GPS-noise values,
    /// not owner-approved balance; the owner spec only says "po wejściu w dozwolony zasięg".
    /// Defaults match <c>AggroGateSettings</c> (10 m accuracy, 1.6× margin) so both gates agree.
    /// </summary>
    public sealed class EncounterEligibilitySettings
    {
        /// <summary>Default AR entry radius (manager decision 2026-10-23 04:12: 25 m, configurable).</summary>
        public const double DefaultEncounterRadiusMeters = 25.0;

        /// <summary>Encounter radius (m) around the mob site inside which "Walcz" is offered. Configurable.</summary>
        public double EncounterRadiusMeters = DefaultEncounterRadiusMeters;

        /// <summary>Fixes worse than this accuracy (m) never enable the action.</summary>
        public double MaxHorizontalAccuracyMeters = 10.0;

        /// <summary>The accuracy circle (accuracy × factor) must lie inside the radius to enable.</summary>
        public double AccuracyMarginFactor = 1.6;

        /// <summary>Once available, stays available until distance − margin exceeds radius + this (m). Stops button flicker.</summary>
        public double ExitHysteresisMeters = 5.0;

        /// <summary>Consecutive distinct in-range fixes needed to enable.</summary>
        public int RequiredStableFixes = 2;

        /// <summary>A fix older than this (s, compared with the caller's clock) disables the action.</summary>
        public double MaxFixAgeSeconds = 10.0;

        /// <summary>
        /// Rule "approach on the map, stop, then AR": AR entry needs the player to be (almost) still. Above this
        /// speed (m/s, noise-reduced estimate from <see cref="MovementSpeedEstimator"/>) the state is
        /// <see cref="EncounterAvailability.Moving"/>. Prototype value.
        /// </summary>
        public double MaxEntrySpeedMetersPerSecond = 0.7;

        /// <summary>
        /// Worst fix accuracy that can still enable AR: the stricter of <see cref="MaxHorizontalAccuracyMeters"/>
        /// and the radius itself (accuracy × margin must fit inside the radius). Worse ⇒ "Słaby GPS".
        /// </summary>
        public double UsableAccuracyLimitMeters =>
            Math.Min(MaxHorizontalAccuracyMeters, EncounterRadiusMeters / Math.Max(1.0, AccuracyMarginFactor));

        /// <summary>Throws when values are unusable (non-positive radius, NaN, …).</summary>
        public void Validate()
        {
            if (!(EncounterRadiusMeters > 0) || double.IsInfinity(EncounterRadiusMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(EncounterRadiusMeters), EncounterRadiusMeters, "Radius must be > 0 m.");
            }

            if (!(MaxHorizontalAccuracyMeters > 0) || !(AccuracyMarginFactor >= 1.0) || !(MaxFixAgeSeconds > 0)
                || !(MaxEntrySpeedMetersPerSecond > 0) || ExitHysteresisMeters < 0 || double.IsNaN(ExitHysteresisMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(EncounterEligibilitySettings), "Invalid eligibility settings.");
            }
        }
    }

    /// <summary>
    /// Snapshot handed from the map to the AR entry (Developer's <c>ArEncounterLauncher</c>). Carries the
    /// simulation flag so reward code can refuse to persist simulated rewards as production rewards.
    /// Contains no coordinates.
    /// </summary>
    public sealed class EncounterTicket
    {
        public EncounterTicket(string mobId, string mobName, int level, double distanceMeters, double accuracyMeters, LocationSourceKind source)
        {
            if (string.IsNullOrWhiteSpace(mobName))
            {
                throw new ArgumentException("Mob name is required.", nameof(mobName));
            }

            MobId = mobId ?? string.Empty;
            MobName = mobName;
            Level = Math.Max(1, level);
            DistanceMeters = distanceMeters;
            AccuracyMeters = accuracyMeters;
            Source = source;
        }

        public string MobId { get; }

        public string MobName { get; }

        public int Level { get; }

        public double DistanceMeters { get; }

        public double AccuracyMeters { get; }

        public LocationSourceKind Source { get; }

        public bool IsSimulated => Source != LocationSourceKind.Gps;

        /// <summary>False for simulated sessions: rewards may be shown (tagged) but must not be saved as production.</summary>
        public bool ProductionRewardsAllowed => Source == LocationSourceKind.Gps;

        /// <summary>Reward tag for logs/UI, e.g. "SYMULACJA".</summary>
        public string RewardTag => IsSimulated ? "SYMULACJA" : string.Empty;

        public override string ToString()
        {
            return "EncounterTicket(" + MobName + " lvl " + Level + ", d=" + DistanceMeters.ToString("0") + " m, acc="
                   + AccuracyMeters.ToString("0") + " m, " + Source + ")";
        }
    }

    /// <summary>
    /// METINAR-042 (#87): decides whether the "Walcz" button is active for one mob, from noisy GPS distance.
    /// Enabled only when the fix is fresh, accurate enough and its uncertainty circle lies inside the
    /// encounter radius for a short stable streak; disabled again with hysteresis. Pure C#, deterministic.
    /// Unlike <c>AggroConfidenceGate</c> (one-shot aggro trigger) this is a continuous availability state.
    /// </summary>
    public sealed class EncounterEligibility
    {
        private readonly EncounterEligibilitySettings _s;
        private bool _hasLastTimestamp;
        private double _lastTimestamp;
        private int _insideCount;

        public EncounterEligibility(EncounterEligibilitySettings settings = null)
        {
            _s = settings ?? new EncounterEligibilitySettings();
            _s.Validate();
        }

        public EncounterEligibilitySettings Settings => _s;

        public EncounterAvailability State { get; private set; } = EncounterAvailability.None;

        public bool IsAvailable => State == EncounterAvailability.Available;

        /// <summary>Metres the player still has to walk (distance + margin − radius); 0 when in range.</summary>
        public double DistanceToEnterMeters { get; private set; }

        public double LastDistanceMeters { get; private set; } = double.PositiveInfinity;

        public double LastAccuracyMeters { get; private set; } = double.PositiveInfinity;

        /// <summary>
        /// Feeds the latest fix (safe to call every frame; repeated timestamps do not count twice).
        /// </summary>
        /// <param name="distanceMeters">Player → mob horizontal distance from the raw (unsmoothed) fix.</param>
        /// <param name="accuracyMeters">Fix horizontal accuracy radius.</param>
        /// <param name="fixTimestampSeconds">Fix timestamp.</param>
        /// <param name="nowSeconds">Current time on the same clock as the fix timestamp.</param>
        /// <param name="siteIsSafe">Result of the encounter-site policy for this mob.</param>
        /// <param name="playerSpeedMetersPerSecond">Noise-reduced walking speed; NaN = unknown (does not block).</param>
        public EncounterAvailability Update(double distanceMeters, double accuracyMeters, double fixTimestampSeconds, double nowSeconds, bool siteIsSafe,
            double playerSpeedMetersPerSecond = double.NaN)
        {
            if (!Finite(distanceMeters) || !Finite(accuracyMeters) || !Finite(fixTimestampSeconds) || !Finite(nowSeconds)
                || distanceMeters < 0 || accuracyMeters < 0)
            {
                _insideCount = 0;
                DistanceToEnterMeters = 0;
                return State = EncounterAvailability.None;
            }

            LastDistanceMeters = distanceMeters;
            LastAccuracyMeters = accuracyMeters;
            bool newFix = !_hasLastTimestamp || fixTimestampSeconds != _lastTimestamp;
            if (_hasLastTimestamp && fixTimestampSeconds < _lastTimestamp)
            {
                _insideCount = 0; // stream restarted
            }

            _hasLastTimestamp = true;
            _lastTimestamp = fixTimestampSeconds;

            double margin = accuracyMeters * _s.AccuracyMarginFactor;
            DistanceToEnterMeters = Math.Max(0, distanceMeters + margin - _s.EncounterRadiusMeters);

            if (!siteIsSafe)
            {
                _insideCount = 0;
                return State = EncounterAvailability.UnsafeSite;
            }

            if (nowSeconds - fixTimestampSeconds > _s.MaxFixAgeSeconds)
            {
                _insideCount = 0;
                return State = EncounterAvailability.StaleFix;
            }

            // Accuracy worse than the radius (or the 10 m cap): AR entry is never offered, show "Słaby GPS".
            if (accuracyMeters > _s.UsableAccuracyLimitMeters)
            {
                _insideCount = 0;
                return State = EncounterAvailability.PoorAccuracy;
            }

            bool moving = Finite(playerSpeedMetersPerSecond) && playerSpeedMetersPerSecond > _s.MaxEntrySpeedMetersPerSecond;

            if (State == EncounterAvailability.Available)
            {
                if (distanceMeters - margin > _s.EncounterRadiusMeters + _s.ExitHysteresisMeters)
                {
                    _insideCount = 0;
                    return State = EncounterAvailability.TooFar;
                }

                if (moving)
                {
                    _insideCount = 0;
                    return State = EncounterAvailability.Moving;
                }

                return State;
            }

            if (distanceMeters + margin > _s.EncounterRadiusMeters)
            {
                _insideCount = 0;
                return State = EncounterAvailability.TooFar;
            }

            if (moving)
            {
                _insideCount = 0;
                return State = EncounterAvailability.Moving;
            }

            if (newFix)
            {
                _insideCount++;
            }

            return State = _insideCount >= Math.Max(1, _s.RequiredStableFixes)
                ? EncounterAvailability.Available
                : (State == EncounterAvailability.Moving ? EncounterAvailability.Moving : EncounterAvailability.TooFar);
        }

        /// <summary>Builds the AR hand-off ticket; returns false unless the action is currently available.</summary>
        public bool TryCreateTicket(string mobId, string mobName, int level, LocationSourceKind source, out EncounterTicket ticket)
        {
            ticket = null;
            if (!IsAvailable)
            {
                return false;
            }

            ticket = new EncounterTicket(mobId, mobName, level, LastDistanceMeters, LastAccuracyMeters, source);
            return true;
        }

        public void Reset()
        {
            _hasLastTimestamp = false;
            _insideCount = 0;
            DistanceToEnterMeters = 0;
            LastDistanceMeters = double.PositiveInfinity;
            LastAccuracyMeters = double.PositiveInfinity;
            State = EncounterAvailability.None;
        }

        private static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
