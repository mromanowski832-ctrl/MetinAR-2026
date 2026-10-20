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
    /// Candidate entities inside a lat/lon box whose stored flag is active and whose spawn window
    /// contains <paramref name="now"/>. Exact radius filtering happens in the caller.
    /// </summary>
    Task<IReadOnlyList<WorldEntity>> ListActiveInBoxAsync(
        double minLat, double maxLat, double minLon, double maxLon, bool ignoreLongitude,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IEncounterStore
{
    Task<EncounterSession?> GetAsync(string encounterId, CancellationToken cancellationToken);

    /// <summary>
    /// Start a session, or return the existing open session for the same (player, entity).
    /// </summary>
    Task<(EncounterSession Session, bool Created)> StartAsync(
        string newEncounterId, string playerId, string worldEntityId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transition an open session to completed. Only one caller can ever observe
    /// <c>FirstCompletion = true</c> for a given encounter; that is the single future reward hook.
    /// </summary>
    Task<(EncounterSession Session, bool FirstCompletion)?> CompleteAsync(
        string encounterId, DateTimeOffset now, CancellationToken cancellationToken);
}
