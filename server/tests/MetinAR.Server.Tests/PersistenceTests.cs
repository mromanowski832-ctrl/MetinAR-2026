using System.Net;
using MetinAR.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MetinAR.Server.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task PlayerAndEncounterState_SurviveServerRestart_AndCompletionStaysIdempotent()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        string encounterId;
        string nonce;

        using (var first = new MetinArServerFactory(db))
        using (HttpClient client = first.CreateClient())
        {
            (await client.PutJsonAsync("/api/v1/player/persist-1", new { displayName = "Persisted", lastKnownPosition = new { latitude = 0.001, longitude = 0.001 } })).EnsureSuccessStatusCode();
            (encounterId, nonce) = await client.StartAtAsync("persist-1", "seed-mob-0001");
            var done = await (await client.CompleteAsync(encounterId, "persist-1", nonce)).JsonAsync();
            Assert.True(done.GetProperty("firstCompletion").GetBoolean());
        }

        Assert.True(File.Exists(db));

        using (var second = new MetinArServerFactory(db, MetinArServerFactory.DefaultNow.AddHours(1)))
        using (HttpClient client = second.CreateClient())
        {
            HttpResponseMessage player = await client.GetAsync("/api/v1/player/persist-1");
            Assert.Equal(HttpStatusCode.OK, player.StatusCode);
            var p = await player.JsonAsync();
            Assert.Equal("Persisted", p.GetProperty("displayName").GetString());
            Assert.Equal(0.001, p.GetProperty("lastKnownPosition").GetProperty("latitude").GetDouble());

            // After a restart a retried completion still must not count as a new completion.
            var retry = await (await client.CompleteAsync(encounterId, "persist-1", nonce)).JsonAsync();
            Assert.False(retry.GetProperty("firstCompletion").GetBoolean());
            Assert.Equal("2026-10-20T00:00:00.000Z", retry.GetProperty("encounter").GetProperty("completedAt").GetString());

            // Seed data is applied once, not duplicated on restart.
            var nearby = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=1000")).JsonAsync();
            Assert.Equal(3, nearby.GetProperty("totalInRadius").GetInt32());
        }
    }

    [Fact]
    public async Task Migrator_AppliesEachTrackedScriptOnce()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var migrator = new SchemaMigrator(connections, TimeProvider.System, NullLogger<SchemaMigrator>.Instance);

        IReadOnlyList<string> first = await migrator.MigrateAsync();
        IReadOnlyList<string> second = await migrator.MigrateAsync();

        Assert.Equal(new[] { "0001_initial_schema", "0002_seed_synthetic_world", "0003_encounter_authority_and_spawn_schedules" }, first);
        Assert.Equal(SchemaMigrator.KnownMigrations, first);
        Assert.Empty(second);

        await using SqliteConnection connection = await connections.OpenAsync();
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM world_entities;";
        Assert.Equal(8L, (long)(await count.ExecuteScalarAsync())!);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Schema_RejectsSecondOpenEncounterForSamePlayerAndEntity()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        await new SchemaMigrator(connections, TimeProvider.System, NullLogger<SchemaMigrator>.Instance).MigrateAsync();

        await using SqliteConnection c = await connections.OpenAsync();
        await using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO player_profiles (id, display_name, level, current_hp, max_hp, current_mp, max_mp, experience, created_at, updated_at)
            VALUES ('p-1', 'P', 1, 1, 1, 0, 0, 0, '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
            INSERT INTO encounter_sessions (id, player_id, world_entity_id, started_at, expires_at, status)
            VALUES ('e-1', 'p-1', 'seed-mob-0001', '2026-01-01T00:00:00.000Z', '2026-01-01T00:10:00.000Z', 'in_progress');
            """;
        await cmd.ExecuteNonQueryAsync();

        await using SqliteCommand dup = c.CreateCommand();
        dup.CommandText = "INSERT INTO encounter_sessions (id, player_id, world_entity_id, started_at, expires_at, status) VALUES ('e-2', 'p-1', 'seed-mob-0001', '2026-01-01T00:00:00.000Z', '2026-01-01T00:10:00.000Z', 'in_progress');";
        await Assert.ThrowsAsync<SqliteException>(() => dup.ExecuteNonQueryAsync());
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Upgrade_FromMetinar010Schema_KeepsRows_AndLegacyOpenSessionCannotBeCompleted()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var migrator = new SchemaMigrator(connections, TimeProvider.System, NullLogger<SchemaMigrator>.Instance);
        Assert.Equal(new[] { "0001_initial_schema", "0002_seed_synthetic_world" }, await migrator.MigrateAsync("0002_seed_synthetic_world", default));

        await using (SqliteConnection c = await connections.OpenAsync())
        await using (SqliteCommand cmd = c.CreateCommand())
        {
            // Rows exactly as METINAR-010 wrote them (no TTL / nonce columns yet).
            cmd.CommandText = """
                INSERT INTO player_profiles (id, display_name, level, current_hp, max_hp, current_mp, max_mp, experience, created_at, updated_at)
                VALUES ('legacy-1', 'Legacy', 1, 100, 100, 100, 100, 0, '2026-10-19T00:00:00.000Z', '2026-10-19T00:00:00.000Z');
                INSERT INTO encounter_sessions (id, player_id, world_entity_id, started_at, completed_at, status) VALUES
                    ('legacy-open', 'legacy-1', 'seed-mob-0001', '2026-10-19T00:00:00.000Z', NULL, 'in_progress'),
                    ('legacy-done', 'legacy-1', 'seed-mob-0002', '2026-10-19T00:00:00.000Z', '2026-10-19T00:01:00.000Z', 'completed');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();

        using var factory = new MetinArServerFactory(db); // startup applies 0003 on top
        using HttpClient client = factory.CreateClient();
        Assert.Equal("Legacy", (await (await client.GetAsync("/api/v1/player/legacy-1")).JsonAsync()).GetProperty("displayName").GetString());

        HttpResponseMessage open = await client.CompleteAsync("legacy-open", "legacy-1", new string('A', 43));
        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode); // legacy rows have no nonce
        Assert.Equal("ENCOUNTER_NONCE_MISMATCH", (await open.JsonAsync()).GetProperty("code").GetString());

        // A fresh start for the same pair is not blocked by the legacy open row: it is past its (zero) TTL.
        (string fresh, _) = await client.StartAtAsync("legacy-1", "seed-mob-0001");
        Assert.NotEqual("legacy-open", fresh);

        await using SqliteConnection check = new($"Data Source={db}");
        await check.OpenAsync();
        await using SqliteCommand q = check.CreateCommand();
        q.CommandText = "SELECT id || ':' || status || ':' || expires_at FROM encounter_sessions WHERE id LIKE 'legacy-%' ORDER BY id;";
        var rows = new List<string>();
        await using (SqliteDataReader r = await q.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                rows.Add(r.GetString(0));
            }
        }

        Assert.Equal(new[] { "legacy-done:completed:2026-10-19T00:00:00.000Z", "legacy-open:expired:2026-10-19T00:00:00.000Z" }, rows);
        SqliteConnection.ClearAllPools();
    }
}
