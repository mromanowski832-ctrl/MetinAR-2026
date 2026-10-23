using MetinAR.Server.SaveGames;

namespace MetinAR.Server.Persistence;

/// <summary>A stored SaveGame whose checksum was verified and which was migrated to the current schema version.</summary>
public sealed record StoredSaveGame(long Revision, SaveGameDocument Document);

public enum SaveWriteStatus
{
    Written,
    RevisionConflict,
}

public enum RewardStoreStatus
{
    /// <summary>This call recorded the reward (the single application for the encounter).</summary>
    Applied,

    /// <summary>The encounter's reward was recorded earlier; nothing changed.</summary>
    AlreadyApplied,

    /// <summary>The rules refused the claim (e.g. no space); nothing was recorded, the client may retry.</summary>
    Rejected,
}

public sealed record RewardStoreResult(
    RewardStoreStatus Status,
    SaveGameDocument? Document,
    SaveRewardGrant? Granted,
    string? StoredClaimJson,
    string? RejectionReason);

/// <summary>The stored SaveGame is unreadable (checksum mismatch, invalid JSON or unsupported version).</summary>
public sealed class SaveGameIntegrityException : Exception
{
    public SaveGameIntegrityException()
    {
    }

    public SaveGameIntegrityException(string message)
        : base(message)
    {
    }

    public SaveGameIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? PlayerId { get; init; }
}

public interface ISaveGameStore
{
    /// <summary>The player's stored SaveGame, or null when none was written yet (revision 0 is virtual).</summary>
    /// <exception cref="SaveGameIntegrityException">The stored row is corrupt; it is left untouched.</exception>
    Task<StoredSaveGame?> GetAsync(string playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Compare-and-set: store <paramref name="document"/> (whose revision must be <paramref name="expectedRevision"/> + 1)
    /// only if the stored revision is still <paramref name="expectedRevision"/>. Appends a history row and mirrors
    /// display name / level / experience into player_profiles.
    /// </summary>
    Task<(SaveWriteStatus Status, long CurrentRevision)> ReplaceAsync(
        SaveGameDocument document, long expectedRevision, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Apply a reward exactly once per encounter, in one write transaction: ledger lookup, current save (or
    /// <paramref name="initialIfMissing"/>), <paramref name="apply"/> (document, next revision), save + history +
    /// ledger + profile mirror. Concurrent calls are serialized; the ledger primary key makes duplicates impossible.
    /// </summary>
    Task<RewardStoreResult> ApplyRewardAsync(
        string encounterId,
        string playerId,
        string claimJson,
        SaveGameDocument initialIfMissing,
        Func<SaveGameDocument, long, RewardApplication> apply,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
