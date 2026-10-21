using MetinAR.Server.Domain;

namespace MetinAR.Server.Persistence;

/// <summary>
/// Storage boundary. Endpoints depend only on these interfaces, so the SQLite prototype can be
/// replaced by PostgreSQL without changing the HTTP contract.
/// </summary>
public interface IPlayerStore
{
    Task<PlayerProfile?> GetAsync(string playerId, CancellationToken cancellationToken);

    /// <summary>Create the profile if missing, otherwise update the client-editable fields.</summary>
    /// <returns>The stored profile and whether it was newly created.</returns>
    Task<(PlayerProfile Profile, bool Created)> UpsertAsync(
        string playerId,
        string displayName,
        GeoPoint? coarsePosition,
        PrototypePlayerDefaults defaults,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IWorldStore
{
    Task<WorldEntity?> GetAsync(string worldEntityId, CancellationToken cancellationToken);

    /// <summary>
    /// Candidate entities inside a lat/lon box whose stored flag is active and whose spawn window contains
    /// <paramref name="now"/>, including their schedule (if any). Schedule phase and exact radius filtering
    /// happen in the caller.
    /// </summary>
    Task<IReadOnlyList<WorldEntity>> ListActiveInBoxAsync(
        double minLat, double maxLat, double minLon, double maxLon, bool ignoreLongitude,
        DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IEncounterStore.CompleteAsync"/>.</summary>
public enum CompletionOutcome
{
    /// <summary>This call moved the session to completed (the single future reward hook).</summary>
    FirstCompletion,

    /// <summary>The session was already completed earlier; nothing changed.</summary>
    AlreadyCompleted,

    /// <summary>The session's TTL ran out before completion; it is (now) recorded as expired.</summary>
    Expired,
}

public interface IEncounterStore
{
    Task<EncounterSession?> GetAsync(string encounterId, CancellationToken cancellationToken);

    /// <summary>SHA-256 (hex) of the session's current nonce, or null for legacy sessions.</summary>
    Task<string?> GetNonceHashAsync(string encounterId, CancellationToken cancellationToken);

    /// <summary>
    /// Start a session, or reuse the open, unexpired session for the same (player, entity). An open session past
    /// its TTL is first transitioned to expired. The stored nonce hash is set (new) or rotated (reuse), so only
    /// the most recently issued nonce can complete the session.
    /// </summary>
    Task<(EncounterSession Session, bool Created)> StartAsync(
        string newEncounterId, string playerId, string worldEntityId, DateTimeOffset now, DateTimeOffset expiresAt,
        string nonceSha256, EncounterStartEvidence evidence, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transition an open, unexpired session to completed (compare-and-set). Exactly one caller can
    /// ever observe <see cref="CompletionOutcome.FirstCompletion"/>. An open session past its TTL is transitioned to
    /// expired instead. Returns null when the session does not exist.
    /// </summary>
    Task<(EncounterSession Session, CompletionOutcome Outcome)?> CompleteAsync(
        string encounterId, DateTimeOffset now, CancellationToken cancellationToken);
}
