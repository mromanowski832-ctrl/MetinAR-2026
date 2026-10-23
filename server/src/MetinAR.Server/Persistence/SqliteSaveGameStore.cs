using System.Text.Json;
using System.Text.Json.Nodes;
using MetinAR.Server.SaveGames;
using Microsoft.Data.Sqlite;

namespace MetinAR.Server.Persistence;

public sealed class SqliteSaveGameStore : ISaveGameStore
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SaveGameMigrator _migrator;

    public SqliteSaveGameStore(SqliteConnectionFactory connections, SaveGameMigrator migrator)
    {
        _connections = connections;
        _migrator = migrator;
    }

    public async Task<StoredSaveGame?> GetAsync(string playerId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, playerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(SaveWriteStatus Status, long CurrentRevision)> ReplaceAsync(
        SaveGameDocument document, long expectedRevision, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Revision != expectedRevision + 1)
        {
            throw new ArgumentException("document.Revision must be expectedRevision + 1.", nameof(document));
        }

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await WriteAsync(connection, transaction, document, expectedRevision, "client_sync", null, now, cancellationToken).ConfigureAwait(false))
        {
            long current = await CurrentRevisionAsync(connection, transaction, document.PlayerId, cancellationToken).ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return (SaveWriteStatus.RevisionConflict, current);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (SaveWriteStatus.Written, document.Revision);
    }

    public async Task<RewardStoreResult> ApplyRewardAsync(
        string encounterId,
        string playerId,
        string claimJson,
        SaveGameDocument initialIfMissing,
        Func<SaveGameDocument, long, RewardApplication> apply,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialIfMissing);
        ArgumentNullException.ThrowIfNull(apply);

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Microsoft.Data.Sqlite starts BEGIN IMMEDIATE: the write lock is taken before the ledger is read, so two
        // concurrent claims for the same encounter are serialized and the second one sees the first one's ledger row.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string? storedClaim = null;
        string? storedGrant = null;
        await using (SqliteCommand ledger = connection.CreateCommand())
        {
            ledger.Transaction = transaction;
            ledger.CommandText = "SELECT claim_json, granted_json FROM encounter_rewards WHERE encounter_id = $id;";
            ledger.Parameters.AddWithValue("$id", encounterId);
            await using SqliteDataReader r = await ledger.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                storedClaim = r.GetString(0);
                storedGrant = r.GetString(1);
            }
        }

        StoredSaveGame? current = await ReadAsync(connection, transaction, playerId, cancellationToken).ConfigureAwait(false);
        SaveGameDocument document = current?.Document ?? initialIfMissing;
        long revision = current?.Revision ?? 0;

        if (storedGrant is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new RewardStoreResult(RewardStoreStatus.AlreadyApplied, document, SaveGameJson.Deserialize<SaveRewardGrant>(storedGrant), storedClaim, null);
        }

        RewardApplication application = apply(document, revision + 1);
        if (application.Document is null || application.Granted is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new RewardStoreResult(RewardStoreStatus.Rejected, document, null, null, application.RejectionReason);
        }

        if (!await WriteAsync(connection, transaction, application.Document, revision, "encounter_reward", encounterId, now, cancellationToken).ConfigureAwait(false))
        {
            // Unreachable while the IMMEDIATE transaction holds the write lock; kept as a hard guard.
            throw new InvalidOperationException("SaveGame revision changed inside a write transaction.");
        }

        string grantJson = SaveGameJson.Serialize(application.Granted);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO encounter_rewards (encounter_id, player_id, claim_json, granted_json, applied_revision, applied_at)
                VALUES ($id, $player, $claim, $granted, $revision, $at);
                """;
            insert.Parameters.AddWithValue("$id", encounterId);
            insert.Parameters.AddWithValue("$player", playerId);
            insert.Parameters.AddWithValue("$claim", claimJson);
            insert.Parameters.AddWithValue("$granted", grantJson);
            insert.Parameters.AddWithValue("$revision", application.Document.Revision);
            insert.Parameters.AddWithValue("$at", UtcTimestamp.ToText(now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new RewardStoreResult(RewardStoreStatus.Applied, application.Document, application.Granted, claimJson, null);
    }

    private async Task<StoredSaveGame?> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction, string playerId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.schema_version, s.revision, s.payload_json, s.payload_sha256, p.display_name
            FROM player_savegames s JOIN player_profiles p ON p.id = s.player_id
            WHERE s.player_id = $id;
            """;
        command.Parameters.AddWithValue("$id", playerId);
        await using SqliteDataReader r = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int schemaVersion = r.GetInt32(0);
        long revision = r.GetInt64(1);
        string payload = r.GetString(2);
        string checksum = r.GetString(3);
        string displayName = r.GetString(4);

        if (!string.Equals(SaveGameJson.Sha256Hex(payload), checksum, StringComparison.Ordinal))
        {
            throw new SaveGameIntegrityException("Stored SaveGame checksum mismatch.") { PlayerId = playerId };
        }

        SaveGameDocument document;
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject json)
            {
                throw new SaveGameIntegrityException("Stored SaveGame is not a JSON object.") { PlayerId = playerId };
            }

            SaveGameVersionCheck check = _migrator.Upgrade(json, schemaVersion, out JsonObject upgraded);
            if (check is SaveGameVersionCheck.TooNew or SaveGameVersionCheck.TooOld)
            {
                throw new SaveGameIntegrityException($"Stored SaveGame schema version {schemaVersion} is not readable by this server.") { PlayerId = playerId };
            }

            document = upgraded.Deserialize<SaveGameDocument>(SaveGameJson.Options)
                ?? throw new SaveGameIntegrityException("Stored SaveGame is null.") { PlayerId = playerId };
        }
        catch (JsonException ex)
        {
            throw new SaveGameIntegrityException("Stored SaveGame is not valid SaveGame JSON.", ex) { PlayerId = playerId };
        }

        if (document.Revision != revision || document.PlayerId != playerId)
        {
            throw new SaveGameIntegrityException("Stored SaveGame header does not match its row.") { PlayerId = playerId };
        }

        // player_profiles.display_name stays the single source for the name (legacy PUT /player can change it).
        return new StoredSaveGame(revision, document with { Profile = document.Profile with { DisplayName = displayName } });
    }

    private static async Task<bool> WriteAsync(
        SqliteConnection connection, SqliteTransaction transaction, SaveGameDocument next, long expectedRevision,
        string reason, string? encounterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string payload = SaveGameJson.Serialize(next);
        string checksum = SaveGameJson.Sha256Hex(payload);
        string at = UtcTimestamp.ToText(now);

        await using (SqliteCommand upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = expectedRevision == 0
                ? """
                  INSERT INTO player_savegames (player_id, schema_version, revision, payload_json, payload_sha256, updated_at)
                  VALUES ($player, $version, $revision, $payload, $sha, $at)
                  ON CONFLICT (player_id) DO NOTHING;
                  """
                : """
                  UPDATE player_savegames
                  SET schema_version = $version, revision = $revision, payload_json = $payload, payload_sha256 = $sha, updated_at = $at
                  WHERE player_id = $player AND revision = $expected;
                  """;
            upsert.Parameters.AddWithValue("$player", next.PlayerId);
            upsert.Parameters.AddWithValue("$version", next.SchemaVersion);
            upsert.Parameters.AddWithValue("$revision", next.Revision);
            upsert.Parameters.AddWithValue("$payload", payload);
            upsert.Parameters.AddWithValue("$sha", checksum);
            upsert.Parameters.AddWithValue("$at", at);
            upsert.Parameters.AddWithValue("$expected", expectedRevision);
            if (await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                return false;
            }
        }

        await using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                INSERT INTO player_savegame_revisions (player_id, revision, schema_version, payload_json, payload_sha256, reason, encounter_id, created_at)
                VALUES ($player, $revision, $version, $payload, $sha, $reason, $encounter, $at);
                """;
            history.Parameters.AddWithValue("$player", next.PlayerId);
            history.Parameters.AddWithValue("$revision", next.Revision);
            history.Parameters.AddWithValue("$version", next.SchemaVersion);
            history.Parameters.AddWithValue("$payload", payload);
            history.Parameters.AddWithValue("$sha", checksum);
            history.Parameters.AddWithValue("$reason", reason);
            history.Parameters.AddWithValue("$encounter", (object?)encounterId ?? DBNull.Value);
            history.Parameters.AddWithValue("$at", at);
            await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (SqliteCommand profile = connection.CreateCommand())
        {
            profile.Transaction = transaction;
            profile.CommandText = """
                UPDATE player_profiles SET display_name = $name, level = $level, experience = $exp, updated_at = $at
                WHERE id = $player;
                """;
            profile.Parameters.AddWithValue("$player", next.PlayerId);
            profile.Parameters.AddWithValue("$name", next.Profile.DisplayName);
            profile.Parameters.AddWithValue("$level", next.Character.Level);
            profile.Parameters.AddWithValue("$exp", next.Character.TotalExp);
            profile.Parameters.AddWithValue("$at", at);
            await profile.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private static async Task<long> CurrentRevisionAsync(SqliteConnection connection, SqliteTransaction transaction, string playerId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM player_savegames WHERE player_id = $id;";
        command.Parameters.AddWithValue("$id", playerId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long revision ? revision : 0;
    }
}
