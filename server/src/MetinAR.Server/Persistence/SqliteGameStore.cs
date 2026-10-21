using MetinAR.Server.Domain;
using Microsoft.Data.Sqlite;

namespace MetinAR.Server.Persistence;

public sealed class SqliteGameStore : IPlayerStore, IWorldStore, IEncounterStore
{
    private const string PlayerColumns =
        "id, display_name, level, current_hp, max_hp, current_mp, max_mp, experience, last_known_latitude, last_known_longitude, last_known_position_at, created_at, updated_at";

    private const string WorldSelect =
        "SELECT e.id, e.type, e.template_id, e.latitude, e.longitude, e.level, e.aggressive, e.active, e.spawned_at, e.despawn_at, " +
        "s.anchor_at, s.period_seconds, s.active_seconds " +
        "FROM world_entities e LEFT JOIN world_entity_schedules s ON s.world_entity_id = e.id";

    private const string EncounterColumns =
        "id, player_id, world_entity_id, started_at, expires_at, completed_at, expired_at, status";

    private readonly SqliteConnectionFactory _connections;

    public SqliteGameStore(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    // ---------------------------------------------------------------- players

    async Task<PlayerProfile?> IPlayerStore.GetAsync(string playerId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetPlayerAsync(connection, null, playerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(PlayerProfile Profile, bool Created)> UpsertAsync(
        string playerId,
        string displayName,
        GeoPoint? coarsePosition,
        PrototypePlayerDefaults defaults,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        string at = UtcTimestamp.ToText(now);

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int inserted;
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO player_profiles ({PlayerColumns})
                VALUES ($id, $name, $level, $maxHp, $maxHp, $maxMp, $maxMp, $xp, $lat, $lon, $posAt, $at, $at)
                ON CONFLICT (id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$id", playerId);
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$level", defaults.Level);
            insert.Parameters.AddWithValue("$maxHp", defaults.MaxHp);
            insert.Parameters.AddWithValue("$maxMp", defaults.MaxMp);
            insert.Parameters.AddWithValue("$xp", defaults.Experience);
            AddPosition(insert, coarsePosition, at);
            insert.Parameters.AddWithValue("$at", at);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (inserted == 0)
        {
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            // Position is only overwritten when the client reports one; omitting it keeps the last value.
            update.CommandText = """
                UPDATE player_profiles
                SET display_name = $name,
                    last_known_latitude = COALESCE($lat, last_known_latitude),
                    last_known_longitude = COALESCE($lon, last_known_longitude),
                    last_known_position_at = COALESCE($posAt, last_known_position_at),
                    updated_at = $at
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$id", playerId);
            update.Parameters.AddWithValue("$name", displayName);
            AddPosition(update, coarsePosition, at);
            update.Parameters.AddWithValue("$at", at);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        PlayerProfile profile = await GetPlayerAsync(connection, transaction, playerId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Player row vanished inside its own transaction.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (profile, inserted == 1);
    }

    private static void AddPosition(SqliteCommand command, GeoPoint? position, string at)
    {
        command.Parameters.AddWithValue("$lat", position is { } p1 ? p1.Latitude : DBNull.Value);
        command.Parameters.AddWithValue("$lon", position is { } p2 ? p2.Longitude : DBNull.Value);
        command.Parameters.AddWithValue("$posAt", position is null ? DBNull.Value : at);
    }

    private static async Task<PlayerProfile?> GetPlayerAsync(SqliteConnection connection, SqliteTransaction? transaction, string playerId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {PlayerColumns} FROM player_profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", playerId);
        await using SqliteDataReader r = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        GeoPoint? position = r.IsDBNull(8) ? null : new GeoPoint(r.GetDouble(8), r.GetDouble(9));
        return new PlayerProfile(
            r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt64(7),
            position,
            r.IsDBNull(10) ? null : UtcTimestamp.Parse(r.GetString(10)),
            UtcTimestamp.Parse(r.GetString(11)),
            UtcTimestamp.Parse(r.GetString(12)));
    }

    // ---------------------------------------------------------------- world

    async Task<WorldEntity?> IWorldStore.GetAsync(string worldEntityId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = WorldSelect + " WHERE e.id = $id;";
        command.Parameters.AddWithValue("$id", worldEntityId);
        await using SqliteDataReader r = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadWorldEntity(r) : null;
    }

    public async Task<IReadOnlyList<WorldEntity>> ListActiveInBoxAsync(
        double minLat, double maxLat, double minLon, double maxLon, bool ignoreLongitude,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = WorldSelect + """

            WHERE e.latitude BETWEEN $minLat AND $maxLat
              AND ($ignoreLon = 1 OR e.longitude BETWEEN $minLon AND $maxLon)
              AND e.active = 1
              AND e.spawned_at <= $now
              AND (e.despawn_at IS NULL OR e.despawn_at > $now);
            """;
        command.Parameters.AddWithValue("$minLat", minLat);
        command.Parameters.AddWithValue("$maxLat", maxLat);
        command.Parameters.AddWithValue("$minLon", minLon);
        command.Parameters.AddWithValue("$maxLon", maxLon);
        command.Parameters.AddWithValue("$ignoreLon", ignoreLongitude ? 1 : 0);
        command.Parameters.AddWithValue("$now", UtcTimestamp.ToText(now));

        var result = new List<WorldEntity>();
        await using SqliteDataReader r = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadWorldEntity(r));
        }

        return result;
    }

    private static WorldEntity ReadWorldEntity(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1) == "npc" ? WorldEntityType.Npc : WorldEntityType.Mob,
        r.GetString(2),
        r.GetDouble(3),
        r.GetDouble(4),
        r.GetInt32(5),
        r.GetInt64(6) == 1,
        r.GetInt64(7) == 1,
        UtcTimestamp.Parse(r.GetString(8)),
        r.IsDBNull(9) ? null : UtcTimestamp.Parse(r.GetString(9)),
        r.IsDBNull(10) ? null : new SpawnSchedule(UtcTimestamp.Parse(r.GetString(10)), r.GetInt32(11), r.GetInt32(12)));

    // ---------------------------------------------------------------- encounters

    async Task<EncounterSession?> IEncounterStore.GetAsync(string encounterId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetEncounterAsync(connection, null, encounterId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetNonceHashAsync(string encounterId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT nonce_sha256 FROM encounter_sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", encounterId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task<(EncounterSession Session, bool Created)> StartAsync(
        string newEncounterId, string playerId, string worldEntityId, DateTimeOffset now, DateTimeOffset expiresAt,
        string nonceSha256, EncounterStartEvidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        string at = UtcTimestamp.ToText(now);

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // 1. An open session whose TTL ran out is closed first, so it can neither be reused nor completed.
        string? staleId;
        await using (SqliteCommand stale = connection.CreateCommand())
        {
            stale.Transaction = transaction;
            stale.CommandText = """
                SELECT id FROM encounter_sessions
                WHERE player_id = $player AND world_entity_id = $entity AND status = 'in_progress' AND expires_at <= $now;
                """;
            stale.Parameters.AddWithValue("$player", playerId);
            stale.Parameters.AddWithValue("$entity", worldEntityId);
            stale.Parameters.AddWithValue("$now", at);
            staleId = await stale.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (staleId is not null)
        {
            await ExpireAsync(connection, transaction, staleId, now, "ttl_elapsed_on_start", cancellationToken).ConfigureAwait(false);
        }

        // 2. Insert a new open session; the partial unique index rejects it when one is already open.
        int inserted;
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO encounter_sessions ({EncounterColumns}, nonce_sha256, start_distance_meters, start_accuracy_meters, start_allowed_meters)
                VALUES ($id, $player, $entity, $at, $expires, NULL, NULL, 'in_progress', $nonce, $dist, $acc, $allowed)
                ON CONFLICT DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$id", newEncounterId);
            insert.Parameters.AddWithValue("$player", playerId);
            insert.Parameters.AddWithValue("$entity", worldEntityId);
            insert.Parameters.AddWithValue("$at", at);
            insert.Parameters.AddWithValue("$expires", UtcTimestamp.ToText(expiresAt));
            AddStartParameters(insert, nonceSha256, evidence);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string sessionId;
        if (inserted == 1)
        {
            sessionId = newEncounterId;
            await RecordTransitionAsync(connection, transaction, sessionId, null, "in_progress", now, "started", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // 3. Reuse the open session: rotate its nonce so only the newest response can complete it.
            await using SqliteCommand open = connection.CreateCommand();
            open.Transaction = transaction;
            open.CommandText = """
                UPDATE encounter_sessions
                SET nonce_sha256 = $nonce, start_distance_meters = $dist, start_accuracy_meters = $acc, start_allowed_meters = $allowed
                WHERE player_id = $player AND world_entity_id = $entity AND status = 'in_progress'
                RETURNING id;
                """;
            open.Parameters.AddWithValue("$player", playerId);
            open.Parameters.AddWithValue("$entity", worldEntityId);
            AddStartParameters(open, nonceSha256, evidence);
            sessionId = await open.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                ?? throw new InvalidOperationException("Encounter insert was rejected but no open session exists.");
        }

        EncounterSession session = await GetEncounterAsync(connection, transaction, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Encounter row vanished inside its own transaction.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (session, inserted == 1);
    }

    private static void AddStartParameters(SqliteCommand command, string nonceSha256, EncounterStartEvidence evidence)
    {
        command.Parameters.AddWithValue("$nonce", nonceSha256);
        command.Parameters.AddWithValue("$dist", Math.Round(evidence.DistanceMeters, 1));
        command.Parameters.AddWithValue("$acc", Math.Round(evidence.AccuracyMeters, 1));
        command.Parameters.AddWithValue("$allowed", Math.Round(evidence.AllowedMeters, 1));
    }

    public async Task<(EncounterSession Session, CompletionOutcome Outcome)?> CompleteAsync(
        string encounterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string at = UtcTimestamp.ToText(now);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int updated;
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            // Compare-and-set: only the call that flips an open, unexpired session sees updated == 1.
            update.CommandText = """
                UPDATE encounter_sessions SET status = 'completed', completed_at = $at
                WHERE id = $id AND status = 'in_progress' AND expires_at > $at;
                """;
            update.Parameters.AddWithValue("$id", encounterId);
            update.Parameters.AddWithValue("$at", at);
            updated = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        CompletionOutcome outcome;
        if (updated == 1)
        {
            await RecordTransitionAsync(connection, transaction, encounterId, "in_progress", "completed", now, "completed", cancellationToken).ConfigureAwait(false);
            outcome = CompletionOutcome.FirstCompletion;
        }
        else
        {
            EncounterSession? current = await GetEncounterAsync(connection, transaction, encounterId, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return null;
            }

            if (current.Status == EncounterStatus.InProgress)
            {
                // Only reachable when the TTL has elapsed (otherwise the compare-and-set above would have won).
                await ExpireAsync(connection, transaction, encounterId, now, "ttl_elapsed_on_complete", cancellationToken).ConfigureAwait(false);
            }

            outcome = current.Status == EncounterStatus.Completed ? CompletionOutcome.AlreadyCompleted : CompletionOutcome.Expired;
        }

        EncounterSession session = await GetEncounterAsync(connection, transaction, encounterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Encounter row vanished inside its own transaction.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (session, outcome);
    }

    /// <summary>Close an open session as expired. <c>expired_at</c> records the TTL deadline, not the detection time.</summary>
    private static async Task ExpireAsync(SqliteConnection connection, SqliteTransaction transaction, string encounterId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        int changed;
        await using (SqliteCommand expire = connection.CreateCommand())
        {
            expire.Transaction = transaction;
            expire.CommandText = """
                UPDATE encounter_sessions SET status = 'expired', expired_at = expires_at
                WHERE id = $id AND status = 'in_progress' AND expires_at <= $now;
                """;
            expire.Parameters.AddWithValue("$id", encounterId);
            expire.Parameters.AddWithValue("$now", UtcTimestamp.ToText(now));
            changed = await expire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (changed == 1)
        {
            await RecordTransitionAsync(connection, transaction, encounterId, "in_progress", "expired", now, reason, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RecordTransitionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string encounterId, string? from, string to,
        DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        await using SqliteCommand audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = """
            INSERT INTO encounter_transitions (id, encounter_id, from_status, to_status, at, reason)
            VALUES ($id, $encounter, $from, $to, $at, $reason);
            """;
        audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        audit.Parameters.AddWithValue("$encounter", encounterId);
        audit.Parameters.AddWithValue("$from", (object?)from ?? DBNull.Value);
        audit.Parameters.AddWithValue("$to", to);
        audit.Parameters.AddWithValue("$at", UtcTimestamp.ToText(now));
        audit.Parameters.AddWithValue("$reason", reason);
        await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EncounterSession?> GetEncounterAsync(SqliteConnection connection, SqliteTransaction? transaction, string encounterId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {EncounterColumns} FROM encounter_sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", encounterId);
        await using SqliteDataReader r = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEncounter(r) : null;
    }

    private static EncounterSession ReadEncounter(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.GetString(2),
        UtcTimestamp.Parse(r.GetString(3)),
        UtcTimestamp.Parse(r.GetString(4)),
        r.IsDBNull(5) ? null : UtcTimestamp.Parse(r.GetString(5)),
        r.IsDBNull(6) ? null : UtcTimestamp.Parse(r.GetString(6)),
        r.GetString(7) switch
        {
            "completed" => EncounterStatus.Completed,
            "expired" => EncounterStatus.Expired,
            _ => EncounterStatus.InProgress,
        });
}
