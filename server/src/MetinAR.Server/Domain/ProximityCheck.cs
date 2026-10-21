using MetinAR.Server.Geo;

namespace MetinAR.Server.Domain;

public enum ProximityVerdict
{
    Allowed,
    AccuracyTooPoor,
    FixStale,
    FixInFuture,
    TooFar,
}

/// <summary>
/// Pure server-side decision whether a reported client fix is close enough to start an encounter with an
/// entity whose position the server owns. Conservative: uncertainty adds at most
/// <see cref="EncounterRules.AccuracyAllowanceCapMeters"/>, and poor or stale fixes are refused outright.
/// </summary>
public static class ProximityCheck
{
    public static (ProximityVerdict Verdict, EncounterStartEvidence Evidence) Evaluate(
        GeoPoint client, double accuracyMeters, DateTimeOffset? capturedAt,
        GeoPoint entity, DateTimeOffset now, EncounterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        double distance = GeoDistance.HaversineMeters(client, entity);
        double allowed = rules.MaxStartDistanceMeters + Math.Min(accuracyMeters, rules.AccuracyAllowanceCapMeters);
        var evidence = new EncounterStartEvidence(distance, accuracyMeters, allowed);

        if (accuracyMeters > rules.MaxAcceptedAccuracyMeters)
        {
            return (ProximityVerdict.AccuracyTooPoor, evidence);
        }

        if (capturedAt is { } at)
        {
            if (at > now.AddSeconds(rules.MaxFutureSkewSeconds))
            {
                return (ProximityVerdict.FixInFuture, evidence);
            }

            if (now - at > TimeSpan.FromSeconds(rules.MaxFixAgeSeconds))
            {
                return (ProximityVerdict.FixStale, evidence);
            }
        }

        return distance <= allowed
            ? (ProximityVerdict.Allowed, evidence)
            : (ProximityVerdict.TooFar, evidence);
    }
}
