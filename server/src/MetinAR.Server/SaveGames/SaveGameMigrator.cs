using System.Text.Json.Nodes;

namespace MetinAR.Server.SaveGames;

public enum SaveGameVersionCheck
{
    Current,
    Migrated,
    TooNew,
    TooOld,
}

/// <summary>
/// Forward-only SaveGame migrations (contract §4): one pure step per version, applied in order vN → vN+1 on the raw
/// JSON before typed deserialization. A document newer than <see cref="CurrentVersion"/> is refused, never downgraded.
/// v1 is the first contract version, so production has no steps yet; tests inject steps to verify the chain.
/// </summary>
public sealed class SaveGameMigrator
{
    public const int LatestVersion = 1;

    /// <summary>Oldest version this build can still read (older documents have no migration path).</summary>
    public const int OldestReadableVersion = 1;

    private readonly IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> _steps;

    public SaveGameMigrator()
        : this(new Dictionary<int, Func<JsonObject, JsonObject>>(), LatestVersion, OldestReadableVersion)
    {
    }

    /// <param name="steps">Key = source version; the step returns the document at version key + 1.</param>
    /// <param name="currentVersion">Version this build writes.</param>
    /// <param name="oldestReadableVersion">Oldest version that can be migrated.</param>
    internal SaveGameMigrator(IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps, int currentVersion, int oldestReadableVersion)
    {
        _steps = steps;
        CurrentVersion = currentVersion;
        OldestReadable = oldestReadableVersion;
    }

    public int CurrentVersion { get; }

    public int OldestReadable { get; }

    public SaveGameVersionCheck Upgrade(JsonObject document, int version, out JsonObject upgraded)
    {
        ArgumentNullException.ThrowIfNull(document);
        upgraded = document;
        if (version > CurrentVersion)
        {
            return SaveGameVersionCheck.TooNew;
        }

        if (version < OldestReadable)
        {
            return SaveGameVersionCheck.TooOld;
        }

        for (int v = version; v < CurrentVersion; v++)
        {
            if (!_steps.TryGetValue(v, out Func<JsonObject, JsonObject>? step))
            {
                return SaveGameVersionCheck.TooOld;
            }

            upgraded = step(upgraded);
            upgraded["schemaVersion"] = v + 1;
        }

        return version == CurrentVersion ? SaveGameVersionCheck.Current : SaveGameVersionCheck.Migrated;
    }
}
