namespace MetinAR.Server.Domain;

/// <summary>
/// Technical prototype thresholds for server-side encounter authority (METINAR-012).
/// These are anti-abuse / GPS-noise limits, NOT final game-balance values. Configure via the
/// <c>EncounterRules</c> section.
/// </summary>
public sealed class EncounterRules
{
    public const string SectionName = "EncounterRules";

    /// <summary>Base distance (m) between the reported client position and the entity that allows a start.</summary>
    public double MaxStartDistanceMeters { get; set; } = 40;

    /// <summary>
    /// Upper bound (m) of extra tolerance granted for GPS uncertainty. The allowance is
    /// min(reported accuracy, this cap), so a worse fix never buys unlimited range.
    /// </summary>
    public double AccuracyAllowanceCapMeters { get; set; } = 20;

    /// <summary>Fixes with a reported horizontal accuracy radius above this (m) are rejected as too poor.</summary>
    public double MaxAcceptedAccuracyMeters { get; set; } = 50;

    /// <summary>When the client sends <c>capturedAt</c>, fixes older than this (s) are rejected as stale.</summary>
    public int MaxFixAgeSeconds { get; set; } = 30;

    /// <summary>Tolerated client clock skew (s) for a <c>capturedAt</c> in the future.</summary>
    public int MaxFutureSkewSeconds { get; set; } = 5;

    /// <summary>Lifetime (s) of an encounter session; completion after this is rejected.</summary>
    public int EncounterTtlSeconds { get; set; } = 600;

    public IEnumerable<string> Validate()
    {
        if (!(MaxStartDistanceMeters > 0))
        {
            yield return $"{nameof(MaxStartDistanceMeters)} must be > 0.";
        }

        if (!(AccuracyAllowanceCapMeters >= 0))
        {
            yield return $"{nameof(AccuracyAllowanceCapMeters)} must be >= 0.";
        }

        if (!(MaxAcceptedAccuracyMeters > 0))
        {
            yield return $"{nameof(MaxAcceptedAccuracyMeters)} must be > 0.";
        }

        if (MaxFixAgeSeconds <= 0)
        {
            yield return $"{nameof(MaxFixAgeSeconds)} must be > 0.";
        }

        if (MaxFutureSkewSeconds < 0)
        {
            yield return $"{nameof(MaxFutureSkewSeconds)} must be >= 0.";
        }

        if (EncounterTtlSeconds <= 0)
        {
            yield return $"{nameof(EncounterTtlSeconds)} must be > 0.";
        }
    }
}
