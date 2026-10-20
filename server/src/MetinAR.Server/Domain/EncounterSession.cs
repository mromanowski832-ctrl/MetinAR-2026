namespace MetinAR.Server.Domain;

public enum EncounterStatus
{
    InProgress,
    Completed,
}

public sealed record EncounterSession(
    string Id,
    string PlayerId,
    string WorldEntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    EncounterStatus Status);
