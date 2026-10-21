using System.Net;
using System.Text.Json;
using MetinAR.Server.Domain;
using Xunit;

namespace MetinAR.Server.Tests;

/// <summary>METINAR-012: deterministic, server-owned spawn activation windows.</summary>
public class SpawnScheduleTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SpawnSchedule FiveOfTen = new(Anchor, 600, 300);

    [Fact]
    public void Evaluate_FollowsTheCycleExactly()
    {
        Assert.Equal((false, Anchor), FiveOfTen.Evaluate(Anchor.AddTicks(-1)));
        Assert.Equal((true, Anchor.AddSeconds(300)), FiveOfTen.Evaluate(Anchor));
        Assert.Equal((true, Anchor.AddSeconds(300)), FiveOfTen.Evaluate(Anchor.AddSeconds(299.999)));
        Assert.Equal((false, Anchor.AddSeconds(600)), FiveOfTen.Evaluate(Anchor.AddSeconds(300)));
        Assert.Equal((false, Anchor.AddSeconds(600)), FiveOfTen.Evaluate(Anchor.AddSeconds(599.999)));
        Assert.Equal((true, Anchor.AddSeconds(900)), FiveOfTen.Evaluate(Anchor.AddSeconds(600)));
    }

    [Fact]
    public void Evaluate_IsDeterministicFarFromAnchor()
    {
        for (int cycle = 0; cycle < 100_000; cycle += 997)
        {
            DateTimeOffset start = Anchor.AddSeconds(600.0 * cycle);
            Assert.Equal((true, start.AddSeconds(300)), FiveOfTen.Evaluate(start.AddSeconds(150)));
            Assert.Equal((false, start.AddSeconds(600)), FiveOfTen.Evaluate(start.AddSeconds(450)));
            Assert.Equal(FiveOfTen.Evaluate(start.AddSeconds(123)), FiveOfTen.Evaluate(start.AddSeconds(123)));
        }
    }

    [Theory]
    [InlineData(600, 600, true)]
    [InlineData(600, 601, false)]
    [InlineData(0, 0, false)]
    [InlineData(600, 0, false)]
    public void IsValid_RejectsImpossibleSchedules(int period, int active, bool valid) =>
        Assert.Equal(valid, SpawnSchedule.IsValid(period, active));

    [Fact]
    public void ActiveUntil_IsTheEarlierOfScheduleEndAndDespawn()
    {
        var entity = new WorldEntity("x-1", WorldEntityType.Mob, "t", 0, 0, 1, false, true, Anchor, Anchor.AddSeconds(200), FiveOfTen);
        Assert.Equal(Anchor.AddSeconds(200), entity.ActiveUntil(Anchor.AddSeconds(10)));
        Assert.Null(entity.ActiveUntil(Anchor.AddSeconds(200)));

        var unbounded = entity with { DespawnAt = null };
        Assert.Equal(Anchor.AddSeconds(300), unbounded.ActiveUntil(Anchor.AddSeconds(10)));
        Assert.Null(unbounded.ActiveUntil(Anchor.AddSeconds(400)));
        Assert.Null((unbounded with { Active = false }).ActiveUntil(Anchor.AddSeconds(10)));
    }

    // Scheduled seed entities sit ~2.2 km south of Null Island: (-0.02, ±0.0002).
    private const string ScheduledArea = "/api/v1/world/nearby?lat=-0.02&lon=0&radiusMeters=100";

    private static async Task<Dictionary<string, string?>> ActiveAsync(HttpClient client)
    {
        var json = await (await client.GetAsync(ScheduledArea)).JsonAsync();
        return json.GetProperty("entities").EnumerateArray().ToDictionary(
            e => e.GetProperty("id").GetString()!,
            e => e.GetProperty("activeUntil").ValueKind == JsonValueKind.Null ? null : e.GetProperty("activeUntil").GetString());
    }

    [Fact]
    public async Task Nearby_ActivationAndDeactivation_FollowTheSeedSchedule()
    {
        using var factory = new MetinArServerFactory(now: new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        using HttpClient client = factory.CreateClient();

        // 00:00 — mob active (first 5 of every 10 min), NPC inactive (active 06:00-18:00 UTC).
        Assert.Equal(new Dictionary<string, string?> { ["seed-mob-0006"] = "2026-10-20T00:05:00.000Z" }, await ActiveAsync(client));

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 0, 5, 0, TimeSpan.Zero));
        Assert.Empty(await ActiveAsync(client));

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 0, 10, 0, TimeSpan.Zero));
        Assert.Equal(new Dictionary<string, string?> { ["seed-mob-0006"] = "2026-10-20T00:15:00.000Z" }, await ActiveAsync(client));

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero));
        Assert.Equal(new Dictionary<string, string?>
        {
            ["seed-mob-0006"] = "2026-10-20T06:05:00.000Z",
            ["seed-npc-0002"] = "2026-10-20T18:00:00.000Z",
        }, await ActiveAsync(client));

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero));
        Assert.Equal(new Dictionary<string, string?> { ["seed-mob-0006"] = "2026-10-20T18:05:00.000Z" }, await ActiveAsync(client));
    }

    [Fact]
    public async Task Nearby_UnscheduledEntities_ReportNoActiveUntil()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        var json = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=100")).JsonAsync();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("entities")[0].GetProperty("activeUntil").ValueKind);
    }

    [Fact]
    public async Task Schedule_IsIdenticalAcrossServerRestarts()
    {
        string db = MetinArServerFactory.NewDatabasePath();
        var at = new DateTimeOffset(2026, 10, 20, 6, 2, 30, TimeSpan.Zero);
        Dictionary<string, string?> first;
        using (var a = new MetinArServerFactory(db, at))
        using (HttpClient c = a.CreateClient())
        {
            first = await ActiveAsync(c);
        }

        using var b = new MetinArServerFactory(db, at);
        using HttpClient client = b.CreateClient();
        Assert.Equal(first, await ActiveAsync(client));
        Assert.Equal(2, first.Count);
    }

    [Fact]
    public async Task EncounterStart_RespectsSchedule()
    {
        using var factory = new MetinArServerFactory(now: new DateTimeOffset(2026, 10, 20, 0, 7, 0, TimeSpan.Zero)); // mob inactive phase
        using HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync("hunter-1");
        object fix = HttpExtensions.FixAt("seed-mob-0006");

        HttpResponseMessage inactive = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = "seed-mob-0006", clientPosition = fix });
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        Assert.Equal("WORLD_ENTITY_INACTIVE", (await inactive.JsonAsync()).GetProperty("code").GetString());

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 0, 10, 0, TimeSpan.Zero));
        HttpResponseMessage active = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = "seed-mob-0006", clientPosition = fix });
        Assert.Equal(HttpStatusCode.Created, active.StatusCode);
    }

    /// <summary>
    /// D3/Q2 (owner decision, #33 comment 6316048203): in v1 an encounter ends only via TTL. The mob's active
    /// window ending mid-fight does not interrupt or expire an open session.
    /// </summary>
    [Fact]
    public async Task Complete_AfterMobWindowEndsMidFight_StillSucceedsWithinTtl()
    {
        // seed-mob-0006 is active 00:10-00:15 UTC; TTL 600 s puts expiresAt at 00:20.
        using var factory = new MetinArServerFactory(now: new DateTimeOffset(2026, 10, 20, 0, 10, 0, TimeSpan.Zero));
        using HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync("hunter-1");
        (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0006");

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 0, 16, 0, TimeSpan.Zero)); // window over, TTL not
        JsonElement nearby = await (await client.GetAsync("/api/v1/world/nearby?lat=-0.02&lon=0&radiusMeters=200")).JsonAsync();
        Assert.DoesNotContain(nearby.GetProperty("entities").EnumerateArray(), e => e.GetProperty("id").GetString() == "seed-mob-0006");

        HttpResponseMessage done = await client.CompleteAsync(id, "hunter-1", nonce);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        JsonElement json = await done.JsonAsync();
        Assert.True(json.GetProperty("firstCompletion").GetBoolean());
        Assert.Equal("completed", json.GetProperty("encounter").GetProperty("status").GetString());
    }

    [Fact]
    public async Task OpenSession_AfterMobWindowEnds_ExpiresOnlyAtTtl()
    {
        using var factory = new MetinArServerFactory(now: new DateTimeOffset(2026, 10, 20, 0, 10, 0, TimeSpan.Zero));
        using HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync("hunter-1");
        (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0006");

        factory.Clock.Set(new DateTimeOffset(2026, 10, 20, 0, 20, 0, TimeSpan.Zero)); // expiresAt (exclusive)
        HttpResponseMessage late = await client.CompleteAsync(id, "hunter-1", nonce);
        Assert.Equal(HttpStatusCode.Gone, late.StatusCode);
        Assert.Equal("ENCOUNTER_EXPIRED", (await late.JsonAsync()).GetProperty("code").GetString());
    }
}
