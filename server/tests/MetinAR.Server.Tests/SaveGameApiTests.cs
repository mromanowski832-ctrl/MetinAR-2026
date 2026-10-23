using System.Net;
using System.Text.Json.Nodes;
using MetinAR.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static MetinAR.Server.Tests.SaveGameTestHelpers;

namespace MetinAR.Server.Tests;

public class SaveGameApiTests
{
    [Fact]
    public async Task Get_NewPlayer_ReturnsDeterministicInitialServerSaveAtRevision0()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p1");

        (string etag, JsonObject save) = await client.GetSaveAsync("save-p1");
        (_, JsonObject again) = await client.GetSaveAsync("save-p1");

        Assert.Equal("\"0\"", etag);
        Assert.Equal(1, (int)save["schemaVersion"]!);
        Assert.Equal("server", (string)save["mode"]!);
        Assert.Equal(0L, (long)save["revision"]!);
        Assert.Equal("save-p1", (string)save["playerId"]!);
        Assert.Equal("Test save-p1", (string)save["profile"]!["displayName"]!);
        Assert.Equal(1, (int)save["character"]!["level"]!);
        Assert.Equal(0L, (long)save["character"]!["totalExp"]!);
        Assert.Equal("warrior", (string)save["character"]!["classId"]!);
        Assert.Equal(24, (int)save["inventory"]!["capacity"]!);
        Assert.Equal(0L, (long)save["currency"]!["talers"]!);
        Assert.Empty(save["encounterResults"]!.AsArray());
        JsonNode weapon = save["equipment"]!.AsArray().Single()!;
        Assert.Equal("weapon", (string)weapon["slot"]!);
        Assert.Equal("starter_sword", (string)weapon["item"]!["definitionId"]!);
        // The virtual initial save is stable until first written.
        Assert.Equal(save.ToJsonString(), again.ToJsonString());
    }

    [Fact]
    public async Task Get_UnknownOrInvalidPlayer_Returns404Or400()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage missing = await client.GetAsync(SaveUrl("nobody-1"));
        HttpResponseMessage invalid = await client.GetAsync(SaveUrl("bad id!"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("PLAYER_NOT_FOUND", await missing.CodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Put_ClientOwnedChanges_AreStored_BumpRevision_AndSurviveRestart()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        using (var first = new MetinArServerFactory(db))
        using (HttpClient client = await first.CreateClient().WithPlayerAsync("save-p2"))
        {
            (string etag, JsonObject save) = await client.GetSaveAsync("save-p2");
            save["profile"]!["displayName"] = "  Renamed Hero  ";
            save["character"]!["hpCurrent"] = 50;
            save["skills"]!["loadout"]!["slot1"] = "warrior_heavy_slash";
            // Move the starter weapon from equipment into bag slot 3 (placement is client-owned).
            JsonNode weapon = save["equipment"]!.AsArray()[0]!["item"]!.DeepClone();
            weapon["locked"] = true;
            save["equipment"] = new JsonArray();
            save["inventory"]!["items"] = new JsonArray(new JsonObject { ["slotIndex"] = 3, ["item"] = weapon });

            HttpResponseMessage put = await client.PutSaveAsync("save-p2", save, etag);
            string text = await put.Content.ReadAsStringAsync();

            Assert.True(put.StatusCode == HttpStatusCode.OK, text);
            Assert.Equal("\"1\"", put.Headers.ETag!.Tag);
            JsonNode stored = JsonNode.Parse(text)!["saveGame"]!;
            Assert.Equal(1L, (long)stored["revision"]!);
            Assert.Equal("server", (string)stored["mode"]!);
            Assert.Equal("Renamed Hero", (string)stored["profile"]!["displayName"]!);
            Assert.Equal("2026-10-20T00:00:00.000Z", (string)stored["updatedAtUtc"]!);
        }

        using (var second = new MetinArServerFactory(db))
        using (HttpClient client = second.CreateClient())
        {
            (string etag, JsonObject save) = await client.GetSaveAsync("save-p2");
            Assert.Equal("\"1\"", etag);
            Assert.Equal(50, (int)save["character"]!["hpCurrent"]!);
            Assert.Equal(3, (int)save["inventory"]!["items"]![0]!["slotIndex"]!);
            Assert.Empty(save["equipment"]!.AsArray());
            var profile = await (await client.GetAsync("/api/v1/player/save-p2")).JsonAsync();
            Assert.Equal("Renamed Hero", profile.GetProperty("displayName").GetString());
        }
    }

    [Fact]
    public async Task Put_Preconditions_428Missing_400Malformed_412Stale()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p3");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p3");

        HttpResponseMessage missing = await client.PutSaveAsync("save-p3", save, null);
        HttpResponseMessage weak = await client.PutSaveAsync("save-p3", save, "W/\"0\"");
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        Assert.Equal("SAVEGAME_PRECONDITION_REQUIRED", await missing.CodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutSaveAsync("save-p3", save, etag)).StatusCode);

        // Same (now stale) revision 0 again.
        HttpResponseMessage stale = await client.PutSaveAsync("save-p3", save, etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var problem = await stale.JsonAsync();
        Assert.Equal("SAVEGAME_REVISION_CONFLICT", problem.GetProperty("code").GetString());
        Assert.Equal(1L, problem.GetProperty("currentRevision").GetInt64());

        // Body revision must equal If-Match.
        save["revision"] = 1;
        HttpResponseMessage mismatch = await client.PutSaveAsync("save-p3", save, "\"2\"");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.True((await mismatch.JsonAsync()).GetProperty("errors").TryGetProperty("revision", out _));
    }

    [Fact]
    public async Task Put_ServerOwnedFieldChanges_Return409WithFieldList_AndNothingIsStored()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p4");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p4");
        save["character"]!["totalExp"] = 200;
        save["character"]!["level"] = 2;
        save["currency"]!["talers"] = 999;
        save["equipment"]!.AsArray()[0]!["item"]!["temper"] = 5;

        HttpResponseMessage put = await client.PutSaveAsync("save-p4", save, etag);

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        var problem = await put.JsonAsync();
        Assert.Equal("SAVEGAME_SERVER_OWNED_FIELD_CHANGED", problem.GetProperty("code").GetString());
        string[] fields = problem.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray();
        Assert.Contains("character.totalExp", fields);
        Assert.Contains("character.level", fields);
        Assert.Contains("currency", fields);
        Assert.Contains("items", fields);
        Assert.Equal("\"0\"", (await client.GetSaveAsync("save-p4")).ETag);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public async Task Put_MalformedBody_Returns400Malformed(string body)
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p5");

        HttpResponseMessage put = await client.PutSaveRawAsync("save-p5", body, "\"0\"");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal("MALFORMED_REQUEST", await put.CodeAsync());
    }

    [Fact]
    public async Task Put_UnknownOrMissingFields_Return400()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p6");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p6");

        JsonObject unknown = save.DeepClone().AsObject();
        unknown["character"]!["godMode"] = true;
        JsonObject missing = save.DeepClone().AsObject();
        missing.Remove("currency");
        JsonObject wrongType = save.DeepClone().AsObject();
        wrongType["character"]!["hpCurrent"] = "lots";

        foreach (JsonObject bad in new[] { unknown, missing, wrongType })
        {
            HttpResponseMessage put = await client.PutSaveAsync("save-p6", bad, etag);
            string text = await put.Content.ReadAsStringAsync();
            Assert.True(put.StatusCode == HttpStatusCode.BadRequest, text);
        }

        Assert.Equal("\"0\"", (await client.GetSaveAsync("save-p6")).ETag);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400WithFieldErrors()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p7");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p7");
        save["profile"]!["displayName"] = "";
        save["character"]!["classId"] = "dragon";
        save["inventory"]!["stacks"] = new JsonArray(
            new JsonObject { ["slotIndex"] = 0, ["definitionId"] = "potion_hp_s", ["quantity"] = 0 },
            new JsonObject { ["slotIndex"] = 0, ["definitionId"] = "antidote", ["quantity"] = 1 },
            new JsonObject { ["slotIndex"] = 99, ["definitionId"] = "antidote", ["quantity"] = 1 });
        save["equipment"]!.AsArray()[0]!["item"]!["instanceId"] = "NOT-A-UUID";

        HttpResponseMessage put = await client.PutSaveAsync("save-p7", save, etag);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var problem = await put.JsonAsync();
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.EnumerateObject().Count() >= 4, errors.ToString());
        Assert.True(errors.TryGetProperty("profile.displayName", out _), errors.ToString());
        Assert.True(errors.TryGetProperty("character.classId", out _), errors.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Put_UnsupportedSchemaVersion_Returns422WithSupportedVersion(int version)
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p8");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p8");
        save["schemaVersion"] = version;

        HttpResponseMessage put = await client.PutSaveAsync("save-p8", save, etag);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        var problem = await put.JsonAsync();
        Assert.Equal("SAVEGAME_SCHEMA_UNSUPPORTED", problem.GetProperty("code").GetString());
        Assert.Equal(version, problem.GetProperty("receivedSchemaVersion").GetInt32());
        Assert.Equal(1, problem.GetProperty("supportedSchemaVersion").GetInt32());
    }

    [Fact]
    public async Task Put_LocalModeSave_IsNeverAcceptedAsOnline()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p9");
        (_, JsonObject save) = await client.GetSaveAsync("save-p9");
        save["mode"] = "local";

        HttpResponseMessage put = await client.PutSaveAsync("save-p9", save, "\"0\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.Equal("SAVEGAME_LOCAL_MODE_NOT_SYNCABLE", await put.CodeAsync());
    }

    [Fact]
    public async Task Put_LocalEncounterResultInServerSave_IsRejected()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p10");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p10");
        save["encounterResults"] = new JsonArray(new JsonObject
        {
            ["encounterId"] = "local-abc123",
            ["source"] = "local",
            ["status"] = "local_unverified",
            ["worldEntityId"] = "seed-mob-0001",
            ["completedAtUtc"] = "2026-10-20T00:00:00.000Z",
            ["reward"] = new JsonObject { ["exp"] = 1, ["talers"] = 1, ["stacks"] = new JsonArray(), ["items"] = new JsonArray(), ["materials"] = new JsonArray() },
        });

        HttpResponseMessage put = await client.PutSaveAsync("save-p10", save, etag);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_OversizedBody_Returns413()
    {
        using var factory = new MetinArServerFactory(settings: new Dictionary<string, string> { ["SaveGameLimits:MaxBodyBytes"] = "1024" });
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p11");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p11");
        save["profile"]!["displayName"] = new string('x', 2000);

        HttpResponseMessage put = await client.PutSaveAsync("save-p11", save, etag);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, put.StatusCode);
        Assert.Equal("PAYLOAD_TOO_LARGE", await put.CodeAsync());
    }

    [Fact]
    public async Task CorruptedStoredSave_IsDetectedByChecksum_Returns500_AndIsNeverOverwrittenOrReset()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p12");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p12");
        Assert.Equal(HttpStatusCode.OK, (await client.PutSaveAsync("save-p12", save, etag)).StatusCode);

        string tampered;
        await using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE player_savegames SET payload_json = replace(payload_json, '\"talers\":0', '\"talers\":999999') WHERE player_id = 'save-p12';";
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            await using SqliteCommand read = connection.CreateCommand();
            read.CommandText = "SELECT payload_json FROM player_savegames WHERE player_id = 'save-p12';";
            tampered = (string)(await read.ExecuteScalarAsync())!;
        }

        Assert.Contains("999999", tampered, StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();

        HttpResponseMessage get = await client.GetAsync(SaveUrl("save-p12"));
        save["revision"] = 1;
        HttpResponseMessage put = await client.PutSaveAsync("save-p12", save, "\"1\"");

        Assert.Equal(HttpStatusCode.InternalServerError, get.StatusCode);
        Assert.Equal("SAVEGAME_INTEGRITY_FAILED", await get.CodeAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, put.StatusCode);
        Assert.Contains(factory.Logs.Lines, l => l.Contains("save-p12", StringComparison.Ordinal) && l.StartsWith("Error", StringComparison.Ordinal));

        await using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand read = connection.CreateCommand();
            read.CommandText = "SELECT payload_json FROM player_savegames WHERE player_id = 'save-p12';";
            Assert.Equal(tampered, (string)(await read.ExecuteScalarAsync())!);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task LegacyDatabase_From0003_UpgradesAndDerivesInitialSaveFromLegacyProfile()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var migrator = new SchemaMigrator(connections, TimeProvider.System, NullLogger<SchemaMigrator>.Instance);
        await migrator.MigrateAsync("0003_encounter_authority_and_spawn_schedules", CancellationToken.None);
        await using (SqliteConnection connection = await connections.OpenAsync())
        {
            await using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO player_profiles (id, display_name, level, current_hp, max_hp, current_mp, max_mp, experience, created_at, updated_at)
                VALUES ('legacy-1', 'Old Timer', 1, 70, 100, 30, 100, 500, '2026-10-19T10:00:00.000Z', '2026-10-19T10:00:00.000Z');
                """;
            await insert.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();

        using var factory = new MetinArServerFactory(db);
        using HttpClient client = factory.CreateClient();
        var health = await (await client.GetAsync("/health")).JsonAsync();
        (string etag, JsonObject save) = await client.GetSaveAsync("legacy-1");

        Assert.Equal("0004_savegame_and_reward_ledger", health.GetProperty("schemaVersion").GetString());
        Assert.Equal("\"0\"", etag);
        Assert.Equal(500L, (long)save["character"]!["totalExp"]!);
        Assert.Equal(3, (int)save["character"]!["level"]!); // exp_curve.csv: level 3 starts at 430, level 4 at 850.
        Assert.Equal(70, (int)save["character"]!["hpCurrent"]!);
        Assert.Equal("2026-10-19T10:00:00.000Z", (string)save["profile"]!["createdAtUtc"]!);
    }

    [Fact]
    public async Task ConcurrentPuts_OnSameRevision_ExactlyOneWins()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("save-p13");
        (string etag, JsonObject save) = await client.GetSaveAsync("save-p13");

        HttpResponseMessage[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
        {
            JsonObject copy = save.DeepClone().AsObject();
            copy["character"]!["hpCurrent"] = i;
            return client.PutSaveAsync("save-p13", copy, etag);
        }));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed));
        Assert.Equal("\"1\"", (await client.GetSaveAsync("save-p13")).ETag);

        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM player_savegame_revisions WHERE player_id = 'save-p13';";
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }
}
