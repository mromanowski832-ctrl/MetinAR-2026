using System;
using MetinAR.Location;

namespace MetinAR.Map
{
    /// <summary>
    /// Conservative ETAP I margins on top of <see cref="SpawnSafetyConfig"/> (whose prototype buffers stay
    /// unchanged per decision D4). Technical prototype values, not owner-approved policy; full rules are S10 #55.
    /// </summary>
    public sealed class EncounterSitePolicySettings
    {
        /// <summary>Extra clearance (m) added to every road/rail/water/building buffer at the site centre.</summary>
        public double ExtraClearanceMeters = 5.0;

        /// <summary>The whole footprint (circle of this radius) around the site must also be Allowed.</summary>
        public double FootprintRadiusMeters = 8.0;

        /// <summary>Number of footprint samples on that circle.</summary>
        public int FootprintSamples = 8;

        /// <summary>Ring step (m) when searching for a safe site near a desired point.</summary>
        public double SearchStepMeters = 4.0;

        /// <summary>
        /// When true, "Walcz" is refused while the player's own fix is on a road, rail track or water
        /// (inside the configured buffers, no extra clearance). Unknown surface at the player is tolerated.
        /// </summary>
        public bool CheckPlayerSpot = true;
    }

    /// <summary>Outcome of <see cref="EncounterSitePolicy"/>; never contains coordinates.</summary>
    public readonly struct EncounterSiteDecision
    {
        public EncounterSiteDecision(bool allowed, SafetyReason reason, string detail)
        {
            Allowed = allowed;
            Reason = reason;
            Detail = detail ?? string.Empty;
        }

        public bool Allowed { get; }

        public SafetyReason Reason { get; }

        /// <summary>"centre", "footprint" or the blocking class; no coordinates.</summary>
        public string Detail { get; }

        public override string ToString()
        {
            return (Allowed ? "Allowed" : "Denied(" + Reason + (Detail.Length > 0 ? ":" + Detail : string.Empty) + ")");
        }
    }

    /// <summary>
    /// METINAR-042 (#87): conservative subset of S10 #55 for ETAP I. A monster encounter site is accepted only
    /// when the centre is Allowed with extra clearance AND every footprint sample is Allowed. Any Blocked
    /// (road, rail, water, building, private/industrial landuse) or Unknown (no data, unclassified surface)
    /// answer denies — default-deny, no fallback spawn. Wraps any <see cref="ISpawnSafetyService"/>.
    /// </summary>
    public sealed class EncounterSitePolicy
    {
        private readonly ISpawnSafetyService _safety;

        public EncounterSitePolicy(ISpawnSafetyService safety, EncounterSitePolicySettings settings = null)
        {
            _safety = safety ?? throw new ArgumentNullException(nameof(safety));
            Settings = settings ?? new EncounterSitePolicySettings();
        }

        public EncounterSitePolicySettings Settings { get; }

        public EncounterSiteDecision EvaluateSite(GeoCoordinate site)
        {
            SafetyResult centre = _safety.Evaluate(site, Settings.ExtraClearanceMeters);
            if (!centre.IsSpawnAllowed)
            {
                return Deny(centre, "centre");
            }

            int n = Math.Max(0, Settings.FootprintSamples);
            double r = Math.Max(0, Settings.FootprintRadiusMeters);
            if (r > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    GeoCoordinate p = GeoMath.OffsetMeters(site, r * Math.Cos(a), r * Math.Sin(a));
                    SafetyResult s = _safety.Evaluate(p, 0);
                    if (!s.IsSpawnAllowed)
                    {
                        return Deny(s, "footprint");
                    }
                }
            }

            return new EncounterSiteDecision(true, SafetyReason.None, string.Empty);
        }

        /// <summary>
        /// Searches rings around <paramref name="desired"/> (deterministic order) for a site that passes
        /// <see cref="EvaluateSite"/>. Returns false (do not spawn) when none is found within the radius.
        /// </summary>
        public bool TryPlace(GeoCoordinate desired, double searchRadiusMeters, out GeoCoordinate site, out EncounterSiteDecision firstRejection)
        {
            site = desired;
            EncounterSiteDecision d = EvaluateSite(desired);
            firstRejection = d;
            if (d.Allowed)
            {
                return true;
            }

            double step = Settings.SearchStepMeters > 0.5 ? Settings.SearchStepMeters : 4.0;
            for (double r = step; r <= searchRadiusMeters + 1e-9; r += step)
            {
                int samples = Math.Max(8, (int)(2 * Math.PI * r / step));
                for (int i = 0; i < samples; i++)
                {
                    double a = 2 * Math.PI * i / samples;
                    GeoCoordinate c = GeoMath.OffsetMeters(desired, r * Math.Cos(a), r * Math.Sin(a));
                    if (EvaluateSite(c).Allowed)
                    {
                        site = c;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Player-spot check for the "Walcz" action: false when the player's fix is on a road, rail or water
        /// (configured buffers, no extra clearance). Unknown surface or building is tolerated (indoor GPS drift);
        /// the AR scene itself still needs a detected floor plane.
        /// </summary>
        public bool IsPlayerSpotAcceptable(GeoCoordinate player, out SafetyReason reason)
        {
            reason = SafetyReason.None;
            if (!Settings.CheckPlayerSpot)
            {
                return true;
            }

            SafetyResult r = _safety.Evaluate(player, 0);
            if (r.Verdict == SafetyVerdict.Blocked
                && (r.Reason == SafetyReason.Road || r.Reason == SafetyReason.Rail || r.Reason == SafetyReason.Water))
            {
                reason = r.Reason;
                return false;
            }

            return true;
        }

        private static EncounterSiteDecision Deny(SafetyResult r, string where)
        {
            SafetyReason reason = r.Reason == SafetyReason.None ? SafetyReason.NoData : r.Reason;
            return new EncounterSiteDecision(false, reason, where + (r.Detail.Length > 0 ? ":" + r.Detail : string.Empty));
        }
    }
}
