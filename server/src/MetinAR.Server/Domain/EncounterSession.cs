namespace MetinAR.Server.Domain;

/// <summary>One-way lifecycle: <c>InProgress</c> → <c>Completed</c> or <c>InProgress</c> → <c>Expired</c>.</summary>
public enum EncounterStatus
{
    InProgress,
    Completed,
    Expired,
}

public sealed record EncounterSession(
    string Id,
    string PlayerId,
    string WorldEntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiredAt,
    EncounterStatus Status)
{
    /// <summary>True when the session is still open but its TTL has run out at <paramref name="now"/>.</summary>
    public bool IsPastExpiry(DateTimeOffset now) => Status == EncounterStatus.InProgress && now >= ExpiresAt;
}

/// <summary>Server-side evidence recorded at encounter start (no client coordinates are stored).</summary>
public sealed record EncounterStartEvidence(double DistanceMeters, double AccuracyMeters, double AllowedMeters);
