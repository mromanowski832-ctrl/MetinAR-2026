using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MetinAR.Server.Tests;

public class EncounterRewardTests
{
    private static readonly object[] OnePotion = [new { definitionId = "potion_hp_s", quantity = 2 }];
    private static readonly object[] OneBoots = [new { definitionId = "starter_boots", itemLevel = 1, rarity = "uncommon" }];
    private static readonly object[] OneOre = [new { definitionId = "ore_shard", quantity = 3 }];

    [Fact]
    public async Task Reward_AfterCompletion_IsAppliedOnce_AndReplaysReturnTheOriginalGrant()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p1");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p1", "seed-mob-0001");
        object claim = SaveGameTestHelpers.Claim(exp: 30, talers: 12, stacks: OnePotion, items: OneBoots, materials: OneOre);

        HttpResponseMessage first = await client.RewardAsync(id, "rw-p1", nonce, claim);
        string firstText = await first.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.OK, firstText);
        JsonNode applied = JsonNode.Parse(firstText)!;
        Assert.True((bool)applied["applied"]!);
        Assert.False((bool)applied["alreadyApplied"]!);
        Assert.Equal(1L, (long)applied["revision"]!);
        Assert.Equal("\"1\"", first.Headers.ETag!.Tag);
        JsonNode save = applied["saveGame"]!;
        Assert.Equal(30L, (long)save["character"]!["totalExp"]!);
        Assert.Equal(12L, (long)save["currency"]!["talers"]!);
        Assert.Equal("potion_hp_s", (string)save["inventory"]!["stacks"]![0]!["definitionId"]!);
        Assert.Equal(0, (int)save["inventory"]!["stacks"]![0]!["slotIndex"]!);
        JsonNode bootSlot = save["inventory"]!["items"]![0]!;
        Assert.Equal(1, (int)bootSlot["slotIndex"]!);
        string instanceId = (string)bootSlot["item"]!["instanceId"]!;
        Assert.Equal(instanceId, (string)applied["granted"]!["items"]![0]!["instanceId"]!);
        Assert.Equal(3, (int)save["materials"]![0]!["quantity"]!);
        JsonNode result = save["encounterResults"]!.AsArray().Single()!;
        Assert.Equal(id, (string)result["encounterId"]!);
        Assert.Equal("server", (string)result["source"]!);
        Assert.Equal("applied", (string)result["status"]!);

        // Same claim again (e.g. response lost on a flaky network): nothing is granted twice.
        JsonNode replay = JsonNode.Parse(await (await client.RewardAsync(id, "rw-p1", nonce, claim)).Content.ReadAsStringAsync())!;
        Assert.False((bool)replay["applied"]!);
        Assert.True((bool)replay["alreadyApplied"]!);
        Assert.True((bool)replay["claimMatchesApplied"]!);
        Assert.Equal(1L, (long)replay["revision"]!);
        Assert.Equal(instanceId, (string)replay["granted"]!["items"]![0]!["instanceId"]!);

        // A different claim for the same encounter does not change anything either.
        HttpResponseMessage greedy = await client.RewardAsync(id, "rw-p1", nonce, SaveGameTestHelpers.Claim(exp: 5000, talers: 5000));
        JsonNode greedyBody = JsonNode.Parse(await greedy.Content.ReadAsStringAsync())!;
        Assert.Equal(HttpStatusCode.OK, greedy.StatusCode);
        Assert.True((bool)greedyBody["alreadyApplied"]!);
        Assert.False((bool)greedyBody["claimMatchesApplied"]!);
        Assert.Equal(12L, (long)greedyBody["granted"]!["talers"]!);

        (string etag, JsonObject now) = await client.GetSaveAsync("rw-p1");
        Assert.Equal("\"1\"", etag);
        Assert.Equal(12L, (long)now["currency"]!["talers"]!);
        var profile = await (await client.GetAsync("/api/v1/player/rw-p1")).JsonAsync();
        Assert.Equal(30L, profile.GetProperty("experience").GetInt64());
    }

    [Fact]
    public async Task Reward_IsIdempotentAcrossRestart()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        string id;
        string nonce;
        object claim = SaveGameTestHelpers.Claim(exp: 10, talers: 3);
        using (var first = new MetinArServerFactory(db))
        using (HttpClient client = await first.CreateClient().WithPlayerAsync("rw-p2"))
        {
            (id, nonce) = await client.CompletedEncounterAsync("rw-p2", "seed-mob-0001");
            Assert.Equal(HttpStatusCode.OK, (await client.RewardAsync(id, "rw-p2", nonce, claim)).StatusCode);
        }

        using var second = new MetinArServerFactory(db);
        using HttpClient again = second.CreateClient();
        JsonNode replay = JsonNode.Parse(await (await again.RewardAsync(id, "rw-p2", nonce, claim)).Content.ReadAsStringAsync())!;
        Assert.True((bool)replay["alreadyApplied"]!);
        Assert.Equal(3L, (long)replay["saveGame"]!["currency"]!["talers"]!);
    }

    [Fact]
    public async Task Reward_RequiresServerCompletion_OwnershipNonceAndLiveEncounter()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p3");
        await client.WithPlayerAsync("rw-other");
        object claim = SaveGameTestHelpers.Claim(exp: 1);

        (string active, string activeNonce) = await client.StartAtAsync("rw-p3", "seed-mob-0001");
        HttpResponseMessage notCompleted = await client.RewardAsync(active, "rw-p3", activeNonce, claim);
        HttpResponseMessage wrongPlayer = await client.RewardAsync(active, "rw-other", activeNonce, claim);
        string otherNonce = (activeNonce[0] == 'A' ? "B" : "A") + activeNonce[1..];
        HttpResponseMessage wrongNonce = await client.RewardAsync(active, "rw-p3", otherNonce, claim);
        HttpResponseMessage unknown = await client.RewardAsync("enc-unknown-1", "rw-p3", activeNonce, claim);

        Assert.Equal(HttpStatusCode.Conflict, notCompleted.StatusCode);
        Assert.Equal("ENCOUNTER_NOT_COMPLETED", await notCompleted.CodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, wrongPlayer.StatusCode);
        Assert.Equal("ENCOUNTER_PLAYER_MISMATCH", await wrongPlayer.CodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, wrongNonce.StatusCode);
        Assert.Equal("ENCOUNTER_NONCE_MISMATCH", await wrongNonce.CodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Let it expire: an expired encounter never yields a reward.
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Gone, (await client.CompleteAsync(active, "rw-p3", activeNonce)).StatusCode);
        HttpResponseMessage expired = await client.RewardAsync(active, "rw-p3", activeNonce, claim);
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("ENCOUNTER_EXPIRED", await expired.CodeAsync());

        Assert.Equal("\"0\"", (await client.GetSaveAsync("rw-p3")).ETag);
    }

    [Fact]
    public async Task Reward_OverPrototypeCaps_Is422AndNotRecorded_SoACorrectedClaimStillApplies()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p4");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p4", "seed-mob-0001");

        HttpResponseMessage tooMuch = await client.RewardAsync(id, "rw-p4", nonce, SaveGameTestHelpers.Claim(exp: 100001, stacks: [new { definitionId = "potion_hp_s", quantity = 100 }]));
        HttpResponseMessage invalid = await client.RewardAsync(id, "rw-p4", nonce, new { exp = -1, talers = 0, stacks = Array.Empty<object>(), items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        var problem = await tooMuch.JsonAsync();
        Assert.Equal("REWARD_CLAIM_REJECTED", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("claim.exp", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("claim.stacks[0].quantity", out _));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        HttpResponseMessage ok = await client.RewardAsync(id, "rw-p4", nonce, SaveGameTestHelpers.Claim(exp: 500));
        JsonNode body = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.True((bool)body["applied"]!);
        Assert.Equal(3, (int)body["saveGame"]!["character"]!["level"]!); // 500 EXP → level 3 per exp_curve.csv
    }

    [Fact]
    public async Task Reward_WhenBagIsFull_OverflowsIntoTheLootStash()
    {
        using var factory = new MetinArServerFactory(settings: new Dictionary<string, string> { ["PrototypeSaveGameDefaults:InventoryCapacity"] = "1" });
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p5");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p5", "seed-mob-0001");
        object[] items =
        [
            new { definitionId = "starter_boots", itemLevel = 1, rarity = "common" },
            new { definitionId = "starter_gloves", itemLevel = 1, rarity = "common" },
        ];

        HttpResponseMessage ok = await client.RewardAsync(id, "rw-p5", nonce, SaveGameTestHelpers.Claim(items: items, stacks: OnePotion));
        string text = await ok.Content.ReadAsStringAsync();

        Assert.True(ok.StatusCode == HttpStatusCode.OK, text);
        JsonNode save = JsonNode.Parse(text)!["saveGame"]!;
        // Grant order is stacks, then items: the potion takes the only bag slot, both items overflow to the stash.
        Assert.Equal("potion_hp_s", (string)save["inventory"]!["stacks"]![0]!["definitionId"]!);
        Assert.Empty(save["inventory"]!["items"]!.AsArray());
        Assert.Equal(2, save["inventory"]!["lootStash"]!["items"]!.AsArray().Count);
        Assert.Empty(save["inventory"]!["lootStash"]!["stacks"]!.AsArray());
    }

    [Fact]
    public async Task ConcurrentClaims_ForTheSameEncounter_AreAppliedExactlyOnce()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p6");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p6", "seed-mob-0001");
        object claim = SaveGameTestHelpers.Claim(exp: 20, talers: 7, stacks: OnePotion);

        HttpResponseMessage[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.RewardAsync(id, "rw-p6", nonce, claim)));
        JsonNode[] bodies = await Task.WhenAll(results.Select(async r => JsonNode.Parse(await r.Content.ReadAsStringAsync())!));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, bodies.Count(b => (bool)b["applied"]!));
        Assert.Equal(9, bodies.Count(b => (bool)b["alreadyApplied"]!));
        (string etag, JsonObject save) = await client.GetSaveAsync("rw-p6");
        Assert.Equal("\"1\"", etag);
        Assert.Equal(7L, (long)save["currency"]!["talers"]!);
        Assert.Equal(2, (int)save["inventory"]!["stacks"]![0]!["quantity"]!);
        Assert.Equal(1L, await CountAsync(factory, "SELECT COUNT(*) FROM encounter_rewards;"));
    }

    [Fact]
    public async Task ConcurrentClaims_ForDifferentEncounters_AllApply_AndSyncMustRebase()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p7");
        (_, JsonObject before) = await client.GetSaveAsync("rw-p7");
        var encounters = new List<(string Id, string Nonce)>();
        foreach (string mob in new[] { "seed-mob-0001", "seed-mob-0002" })
        {
            encounters.Add(await client.CompletedEncounterAsync("rw-p7", mob));
        }

        HttpResponseMessage[] results = await Task.WhenAll(encounters.Select(e =>
            client.RewardAsync(e.Id, "rw-p7", e.Nonce, SaveGameTestHelpers.Claim(talers: 5, stacks: OnePotion))));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        (string etag, JsonObject save) = await client.GetSaveAsync("rw-p7");
        Assert.Equal("\"2\"", etag);
        Assert.Equal(10L, (long)save["currency"]!["talers"]!);
        Assert.Equal(4, (int)save["inventory"]!["stacks"]![0]!["quantity"]!);
        Assert.Equal(2, save["encounterResults"]!.AsArray().Count);

        // A client that still holds revision 0 must re-read before syncing.
        HttpResponseMessage stale = await client.PutSaveAsync("rw-p7", before, "\"0\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        save["skills"]!["loadout"]!["slot2"] = "warrior_heavy_slash";
        Assert.Equal(HttpStatusCode.OK, (await client.PutSaveAsync("rw-p7", save, etag)).StatusCode);
    }

    private static async Task<long> CountAsync(MetinArServerFactory factory, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        long count = (long)(await command.ExecuteScalarAsync())!;
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
        return count;
    }
}
