using MetinAR.Server.Domain;
using Microsoft.Data.Sqlite;

namespace MetinAR.Server.Persistence;

public sealed class SqliteGameStore : IPlayerStore, IWorldStore, IEncounterStore
{
    private const string PlayerColumns =
        "id, display_name, level, current_hp, max_hp, current_mp, max_mp, experience, last_known_latitude, last_known_longitude, last_known_position_at, created_at, updated_at";

    private const string WorldColumns =
        "id, type, template_id, latitude, longitude, level, aggressive, active, spawned_at, despawn_at";

    private const string EncounterColumns =
        "id, player_id, world_entity_id, started_at, completed_at, status";

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
        command.CommandText = $"SELECT {WorldColumns} FROM world_entities WHERE id = $id;";
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
        command.CommandText = $"""
            SELECT {WorldColumns} FROM world_entities
            WHERE latitude BETWEEN $minLat AND $maxLat
              AND ($ignoreLon = 1 OR longitude BETWEEN $minLon AND $maxLon)
              AND active = 1
              AND spawned_at <= $now
              AND (despawn_at IS NULL OR despawn_at > $now);
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
        r.IsDBNull(9) ? null : UtcTimestamp.Parse(r.GetString(9)));

    // ---------------------------------------------------------------- encounters

    async Task<EncounterSession?> IEncounterStore.GetAsync(string encounterId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetEncounterAsync(connection, null, encounterId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(EncounterSession Session, bool Created)> StartAsync(
        string newEncounterId, string playerId, string worldEntityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int inserted;
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO encounter_sessions ({EncounterColumns})
                VALUES ($id, $player, $entity, $at, NULL, 'in_progress')
                ON CONFLICT DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$id", newEncounterId);
            insert.Parameters.AddWithValue("$player", playerId);
            insert.Parameters.AddWithValue("$entity", worldEntityId);
            insert.Parameters.AddWithValue("$at", UtcTimestamp.ToText(now));
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        EncounterSession session;
        if (inserted == 1)
        {
            session = await GetEncounterAsync(connection, transaction, newEncounterId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Encounter row vanished inside its own transaction.");
        }
        else
        {
            await using SqliteCommand open = connection.CreateCommand();
            open.Transaction = transaction;
            open.CommandText = $"SELECT {EncounterColumns} FROM encounter_sessions WHERE player_id = $player AND world_entity_id = $entity AND status = 'in_progress';";
            open.Parameters.AddWithValue("$player", playerId);
            open.Parameters.AddWithValue("$entity", worldEntityId);
            await using SqliteDataReader r = await open.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            session = await r.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadEncounter(r)
                : throw new InvalidOperationException("Encounter insert was rejected but no open session exists.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (session, inserted == 1);
    }

    public async Task<(EncounterSession Session, bool FirstCompletion)?> CompleteAsync(
        string encounterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int updated;
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            // Compare-and-set: only the call that flips in_progress -> completed sees updated == 1.
            update.CommandText = "UPDATE encounter_sessions SET status = 'completed', completed_at = $at WHERE id = $id AND status = 'in_progress';";
            update.Parameters.AddWithValue("$id", encounterId);
            update.Parameters.AddWithValue("$at", UtcTimestamp.ToText(now));
            updated = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        EncounterSession? session = await GetEncounterAsync(connection, transaction, encounterId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return session is null ? null : (session, updated == 1);
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
        r.IsDBNull(4) ? null : UtcTimestamp.Parse(r.GetString(4)),
        r.GetString(5) == "completed" ? EncounterStatus.Completed : EncounterStatus.InProgress);
}
