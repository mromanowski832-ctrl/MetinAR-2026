using System.Net;
using System.Text.Json.Nodes;
using MetinAR.Server.Persistence;
using MetinAR.Server.SaveGames;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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
        // No separate material bag (#71 decision 4): materials are ordinary inventory stacks.
        Assert.Null(save["materials"]);
        Assert.Equal("ore_shard", (string)save["inventory"]!["stacks"]![1]!["definitionId"]!);
        Assert.Equal(1, (int)save["inventory"]!["stacks"]![1]!["slotIndex"]!);
        Assert.Equal(3, (int)save["inventory"]!["stacks"]![1]!["quantity"]!);
        JsonNode bootSlot = save["inventory"]!["items"]![0]!;
        Assert.Equal(2, (int)bootSlot["slotIndex"]!);
        string instanceId = (string)bootSlot["item"]!["instanceId"]!;
        Assert.Equal(instanceId, (string)applied["granted"]!["items"]![0]!["instanceId"]!);
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

    /// <summary>Write a save straight to the store (revision 1), e.g. a full bag the HTTP API cannot produce quickly.</summary>
    private static async Task SeedSaveAsync(MetinArServerFactory factory, HttpClient client, string playerId, Func<SaveGameDocument, SaveGameDocument> change)
    {
        (_, JsonObject initial) = await client.GetSaveAsync(playerId);
        SaveGameDocument document = change(SaveGameJson.Deserialize<SaveGameDocument>(initial.ToJsonString())) with { Revision = 1 };
        ISaveGameStore store = factory.Services.GetRequiredService<ISaveGameStore>();
        (SaveWriteStatus status, _) = await store.ReplaceAsync(document, 0, factory.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(SaveWriteStatus.Written, status);
    }

    private static SaveGameDocument FullBag(SaveGameDocument d) => d with
    {
        Inventory = d.Inventory with
        {
            Stacks = Enumerable.Range(0, SaveGameRules.InventoryStartCapacity)
                .Select(i => new SaveSlottedStackEntry { SlotIndex = i, DefinitionId = "potion_hp_s", Quantity = 50 }).ToArray(),
        },
    };

    [Fact]
    public async Task Reward_QuantityAboveTheStackLimit_IsSplitIntoFullStacks()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p5");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p5", "seed-mob-0001");

        HttpResponseMessage ok = await client.RewardAsync(id, "rw-p5", nonce,
            SaveGameTestHelpers.Claim(stacks: [new { definitionId = "potion_hp_s", quantity = 60 }]));
        string text = await ok.Content.ReadAsStringAsync();

        Assert.True(ok.StatusCode == HttpStatusCode.OK, text);
        JsonArray stacks = JsonNode.Parse(text)!["saveGame"]!["inventory"]!["stacks"]!.AsArray();
        // potion_hp_s stacks to 50 (items_starter.csv, ITEMS_COMBAT_SPEC §2).
        Assert.Equal([50, 10], stacks.Select(n => (int)n!["quantity"]!));
        Assert.Equal([0, 1], stacks.Select(n => (int)n!["slotIndex"]!));
    }

    // After-24 h behaviour is OPEN for the owner (#71 §11.7, ITEMS_COMBAT_SPEC L116). Default config = Keep: never lost.
    [Theory]
    [InlineData(null, false)]
    [InlineData("Keep", false)]
    [InlineData("Expire", true)]
    public async Task Reward_WhenBagIsFull_GoesToTheLootStash_AfterExpiryFollowsTheConfiguredBehaviour(string? afterExpiry, bool lostAfter24Hours)
    {
        Dictionary<string, string> settings = afterExpiry is null ? [] : new() { ["LootStash:AfterExpiry"] = afterExpiry };
        using var factory = new MetinArServerFactory(settings: settings);
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p7");
        await SeedSaveAsync(factory, client, "rw-p7", FullBag);
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p7", "seed-mob-0001");
        object[] items = [new { definitionId = "starter_boots", itemLevel = 1, rarity = "common" }];

        HttpResponseMessage ok = await client.RewardAsync(id, "rw-p7", nonce,
            SaveGameTestHelpers.Claim(items: items, stacks: [new { definitionId = "potion_hp_s", quantity = 60 }], materials: OneOre));
        string text = await ok.Content.ReadAsStringAsync();

        Assert.True(ok.StatusCode == HttpStatusCode.OK, text);
        JsonNode inventory = JsonNode.Parse(text)!["saveGame"]!["inventory"]!;
        Assert.Equal(24, inventory["stacks"]!.AsArray().Count);
        Assert.All(inventory["stacks"]!.AsArray(), n => Assert.Equal(50, (int)n!["quantity"]!));
        JsonArray stashStacks = inventory["lootStash"]!["stacks"]!.AsArray();
        Assert.Equal([50, 10, 3], stashStacks.Select(n => (int)n!["quantity"]!));
        Assert.Equal("ore_shard", (string)stashStacks[2]!["definitionId"]!);
        JsonNode stashItem = inventory["lootStash"]!["items"]!.AsArray().Single()!;
        Assert.Equal("starter_boots", (string)stashItem["item"]!["definitionId"]!);
        string expires = (string)stashItem["expiresAtUtc"]!;
        Assert.Equal("2026-10-21T00:00:00.000Z", expires); // DefaultNow + 24 h
        Assert.All(stashStacks, n => Assert.Equal(expires, (string)n!["expiresAtUtc"]!));

        factory.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(4, CountStash((await client.GetSaveAsync("rw-p7")).SaveGame));

        factory.Clock.Advance(TimeSpan.FromHours(1));
        (string etag, JsonObject after) = await client.GetSaveAsync("rw-p7");
        Assert.Equal(lostAfter24Hours ? 0 : 4, CountStash(after));

        // The client sending the GET view back is not a stash change; with Expire the next write stores the pruned stash.
        HttpResponseMessage put = await client.PutSaveAsync("rw-p7", after, etag);
        Assert.True(put.StatusCode == HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        factory.Clock.Advance(TimeSpan.FromDays(30));
        (_, JsonObject later) = await client.GetSaveAsync("rw-p7");
        Assert.Equal(lostAfter24Hours ? 0 : 4, CountStash(later));
        SaveGameDocument stored = (await factory.Services.GetRequiredService<ISaveGameStore>().GetAsync("rw-p7", default))!.Document;
        Assert.Equal(lostAfter24Hours ? 0 : 4, stored.Inventory.LootStash.Stacks.Count + stored.Inventory.LootStash.Items.Count);
    }

    [Fact]
    public async Task Put_MovingAWholeEntryFromTheLootStashIntoTheBag_IsAccepted()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p11");
        DateTimeOffset expires = factory.Clock.GetUtcNow().AddHours(24);
        await SeedSaveAsync(factory, client, "rw-p11", d => d with
        {
            Inventory = d.Inventory with
            {
                LootStash = new SaveLootStash
                {
                    Stacks = [new SaveStashStackEntry { DefinitionId = "ore_shard", Quantity = 7, ExpiresAtUtc = expires }],
                    Items = [],
                },
            },
        });
        (string etag, JsonObject save) = await client.GetSaveAsync("rw-p11");
        save["inventory"]!["lootStash"]!["stacks"] = new JsonArray();
        save["inventory"]!["stacks"] = new JsonArray(new JsonObject { ["slotIndex"] = 0, ["definitionId"] = "ore_shard", ["quantity"] = 7 });

        HttpResponseMessage put = await client.PutSaveAsync("rw-p11", save, etag);

        Assert.True(put.StatusCode == HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }

    private static int CountStash(JsonNode save) =>
        save["inventory"]!["lootStash"]!["stacks"]!.AsArray().Count + save["inventory"]!["lootStash"]!["items"]!.AsArray().Count;

    [Fact]
    public async Task Reward_WhenBagAndLootStashAreFull_Returns409RewardNoSpace_AndNothingIsRecorded()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p8");
        DateTimeOffset expires = factory.Clock.GetUtcNow().AddHours(20);
        await SeedSaveAsync(factory, client, "rw-p8", d => FullBag(d) with
        {
            Inventory = FullBag(d).Inventory with
            {
                LootStash = new SaveLootStash
                {
                    Stacks = Enumerable.Range(0, SaveGameRules.LootStashSlots)
                        .Select(_ => new SaveStashStackEntry { DefinitionId = "antidote", Quantity = 50, ExpiresAtUtc = expires }).ToArray(),
                    Items = [],
                },
            },
        });
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p8", "seed-mob-0001");
        object claim = SaveGameTestHelpers.Claim(exp: 10, items: OneBoots);

        HttpResponseMessage full = await client.RewardAsync(id, "rw-p8", nonce, claim);

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("REWARD_NO_SPACE", await full.CodeAsync());
        (string etag, JsonObject save) = await client.GetSaveAsync("rw-p8");
        Assert.Equal("\"1\"", etag);
        Assert.Equal(0L, (long)save["character"]!["totalExp"]!);

        // EXP and talers alone need no space and are still granted.
        HttpResponseMessage expOnly = await client.RewardAsync(id, "rw-p8", nonce, SaveGameTestHelpers.Claim(exp: 10));
        Assert.True(expOnly.StatusCode == HttpStatusCode.OK, await expOnly.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("stacks", "no_such_item")]
    [InlineData("stacks", "starter_boots")]
    [InlineData("stacks", "ore_shard")]
    [InlineData("materials", "potion_hp_s")]
    [InlineData("items", "potion_hp_s")]
    [InlineData("items", "no_such_item")]
    public async Task Reward_DefinitionIdNotInTheCatalogOrInTheWrongList_Returns400(string list, string definitionId)
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p9");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p9", "seed-mob-0001");
        object[] stack = [new { definitionId, quantity = 1 }];
        object[] item = [new { definitionId, itemLevel = 1, rarity = "common" }];
        object claim = list switch
        {
            "stacks" => SaveGameTestHelpers.Claim(stacks: stack),
            "materials" => SaveGameTestHelpers.Claim(materials: stack),
            _ => SaveGameTestHelpers.Claim(items: item),
        };

        HttpResponseMessage r = await client.RewardAsync(id, "rw-p9", nonce, claim);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var problem = await r.JsonAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty($"claim.{list}[0].definitionId", out _), problem.ToString());
    }

    [Fact]
    public async Task Reward_LevelUp_GrantsTwoStatPointsAndOneSkillPointPerLevel()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = await factory.CreateClient().WithPlayerAsync("rw-p10");
        (string id, string nonce) = await client.CompletedEncounterAsync("rw-p10", "seed-mob-0001");

        HttpResponseMessage ok = await client.RewardAsync(id, "rw-p10", nonce, SaveGameTestHelpers.Claim(exp: 500));
        JsonNode save = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!["saveGame"]!;

        Assert.Equal(3, (int)save["character"]!["level"]!);
        Assert.Equal(4, (int)save["stats"]!["freeStatPoints"]!);
        Assert.Equal(2, (int)save["skills"]!["freeSkillPoints"]!);
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
