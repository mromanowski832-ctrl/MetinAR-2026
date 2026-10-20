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

        using (var first = new MetinArServerFactory(db))
        using (HttpClient client = first.CreateClient())
        {
            (await client.PutJsonAsync("/api/v1/player/persist-1", new { displayName = "Persisted", lastKnownPosition = new { latitude = 0.001, longitude = 0.001 } })).EnsureSuccessStatusCode();
            HttpResponseMessage start = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "persist-1", worldEntityId = "seed-mob-0001" });
            encounterId = (await start.JsonAsync()).GetProperty("encounter").GetProperty("id").GetString()!;
            var done = await (await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId, playerId = "persist-1" })).JsonAsync();
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
            var retry = await (await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId, playerId = "persist-1" })).JsonAsync();
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

        Assert.Equal(new[] { "0001_initial_schema", "0002_seed_synthetic_world" }, first);
        Assert.Equal(SchemaMigrator.KnownMigrations, first);
        Assert.Empty(second);

        await using SqliteConnection connection = await connections.OpenAsync();
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM world_entities;";
        Assert.Equal(6L, (long)(await count.ExecuteScalarAsync())!);
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
            INSERT INTO encounter_sessions (id, player_id, world_entity_id, started_at, completed_at, status)
            VALUES ('e-1', 'p-1', 'seed-mob-0001', '2026-01-01T00:00:00.000Z', NULL, 'in_progress');
            """;
        await cmd.ExecuteNonQueryAsync();

        await using SqliteCommand dup = c.CreateCommand();
        dup.CommandText = "INSERT INTO encounter_sessions (id, player_id, world_entity_id, started_at, completed_at, status) VALUES ('e-2', 'p-1', 'seed-mob-0001', '2026-01-01T00:00:00.000Z', NULL, 'in_progress');";
        await Assert.ThrowsAsync<SqliteException>(() => dup.ExecuteNonQueryAsync());
        SqliteConnection.ClearAllPools();
    }
}
