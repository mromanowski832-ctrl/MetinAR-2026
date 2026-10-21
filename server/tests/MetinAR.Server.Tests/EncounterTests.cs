using System.Net;
using System.Text.Json;
using Xunit;

namespace MetinAR.Server.Tests;

// METINAR-010 encounter behaviour. Since METINAR-012 every start carries a client fix and every complete
// carries the start nonce; the assertions below are otherwise unchanged from METINAR-010.
public class EncounterTests
{
    private const string DummyNonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static async Task<(MetinArServerFactory Factory, HttpClient Client)> ArrangeAsync(string playerId = "hunter-1")
    {
        var factory = new MetinArServerFactory();
        HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync(playerId);
        return (factory, client);
    }

    // ------------------------------------------------------------- start validation

    [Fact]
    public async Task Start_ActiveEntity_CreatesInProgressSession()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start",
                new { playerId = "hunter-1", worldEntityId = "seed-mob-0001", clientPosition = HttpExtensions.NearFix() });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.JsonAsync();
            Assert.True(json.GetProperty("created").GetBoolean());
            Assert.Equal(43, json.GetProperty("nonce").GetString()!.Length);
            JsonElement e = json.GetProperty("encounter");
            Assert.Equal("hunter-1", e.GetProperty("playerId").GetString());
            Assert.Equal("seed-mob-0001", e.GetProperty("worldEntityId").GetString());
            Assert.Equal("in_progress", e.GetProperty("status").GetString());
            Assert.Equal("2026-10-20T00:00:00.000Z", e.GetProperty("startedAt").GetString());
            Assert.Equal("2026-10-20T00:10:00.000Z", e.GetProperty("expiresAt").GetString());
            Assert.Equal(JsonValueKind.Null, e.GetProperty("completedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, e.GetProperty("expiredAt").ValueKind);
            Assert.Equal($"/api/v1/encounters/{e.GetProperty("id").GetString()}", response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task Start_UnknownEntity_Returns404()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start",
                new { playerId = "hunter-1", worldEntityId = "seed-mob-9999", clientPosition = HttpExtensions.NearFix() });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("WORLD_ENTITY_NOT_FOUND", (await response.JsonAsync()).GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("seed-mob-0003")] // stored flag inactive
    [InlineData("seed-mob-0004")] // past despawnAt
    public async Task Start_InactiveEntity_Returns409(string entityId)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start",
                new { playerId = "hunter-1", worldEntityId = entityId, clientPosition = HttpExtensions.FixAt(entityId) });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("WORLD_ENTITY_INACTIVE", (await response.JsonAsync()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Start_UnknownPlayer_Returns404()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start",
            new { playerId = "ghost-player", worldEntityId = "seed-mob-0001", clientPosition = HttpExtensions.NearFix() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PLAYER_NOT_FOUND", (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("""{"worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0.0005,"longitude":0.0005,"horizontalAccuracyMeters":5}}""", "playerId")]
    [InlineData("""{"playerId":"hunter-1","clientPosition":{"latitude":0.0005,"longitude":0.0005,"horizontalAccuracyMeters":5}}""", "worldEntityId")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0,"longitude":200,"horizontalAccuracyMeters":5}}""", "clientPosition.longitude")]
    public async Task Start_ValidatesBody(string body, string field)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Post, "/api/v1/encounters/start", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var json = await response.JsonAsync();
            Assert.Equal("VALIDATION_FAILED", json.GetProperty("code").GetString());
            Assert.True(json.GetProperty("errors").TryGetProperty(field, out _));
        }
    }

    [Fact]
    public async Task Start_Twice_ReusesOpenSession_ThenNewSessionAfterCompletion()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string first, string firstNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");

            HttpResponseMessage again = await client.PostJsonAsync("/api/v1/encounters/start",
                new { playerId = "hunter-1", worldEntityId = "seed-mob-0001", clientPosition = HttpExtensions.NearFix() });
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var againJson = await again.JsonAsync();
            Assert.False(againJson.GetProperty("created").GetBoolean());
            Assert.Equal(first, againJson.GetProperty("encounter").GetProperty("id").GetString());
            string rotated = againJson.GetProperty("nonce").GetString()!;
            Assert.NotEqual(firstNonce, rotated);

            (await client.CompleteAsync(first, "hunter-1", rotated)).EnsureSuccessStatusCode();
            (string second, _) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            Assert.NotEqual(first, second);
        }
    }

    // ------------------------------------------------------------- idempotent completion

    [Fact]
    public async Task Complete_IsIdempotent_OnlyFirstCallReportsFirstCompletion()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(42));

            var first = await (await client.CompleteAsync(id, "hunter-1", nonce)).JsonAsync();
            Assert.True(first.GetProperty("firstCompletion").GetBoolean());
            Assert.Equal("completed", first.GetProperty("encounter").GetProperty("status").GetString());
            Assert.Equal("2026-10-20T00:00:42.000Z", first.GetProperty("encounter").GetProperty("completedAt").GetString());

            // Even after the TTL has passed, a retry of an already completed session stays an idempotent 200.
            factory.Clock.Advance(TimeSpan.FromMinutes(30));
            HttpResponseMessage secondResponse = await client.CompleteAsync(id, "hunter-1", nonce);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var second = await secondResponse.JsonAsync();
            Assert.False(second.GetProperty("firstCompletion").GetBoolean());
            Assert.Equal("2026-10-20T00:00:42.000Z", second.GetProperty("encounter").GetProperty("completedAt").GetString());
        }
    }

    [Fact]
    public async Task Complete_ConcurrentRetries_ExactlyOneFirstCompletion()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0002");

            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.CompleteAsync(id, "hunter-1", nonce)));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            JsonElement[] bodies = await Task.WhenAll(responses.Select(r => r.JsonAsync()));
            Assert.Equal(1, bodies.Count(b => b.GetProperty("firstCompletion").GetBoolean()));
            Assert.Single(bodies.Select(b => b.GetProperty("encounter").GetProperty("completedAt").GetString()).Distinct());
        }
    }

    [Fact]
    public async Task Complete_UnknownEncounter_Returns404()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.CompleteAsync("0000000000000000", "hunter-1", DummyNonce);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("ENCOUNTER_NOT_FOUND", (await response.JsonAsync()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Complete_ByDifferentPlayer_Returns403_AndDoesNotComplete()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            await client.WithPlayerAsync("hunter-2");
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");

            HttpResponseMessage response = await client.CompleteAsync(id, "hunter-2", nonce);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("ENCOUNTER_PLAYER_MISMATCH", (await response.JsonAsync()).GetProperty("code").GetString());

            var owner = await (await client.CompleteAsync(id, "hunter-1", nonce)).JsonAsync();
            Assert.True(owner.GetProperty("firstCompletion").GetBoolean());
        }
    }
}
