using System.Net;
using System.Text.Json;
using Xunit;

namespace MetinAR.Server.Tests;

public class EncounterTests
{
    private static async Task<(MetinArServerFactory Factory, HttpClient Client)> ArrangeAsync(string playerId = "hunter-1")
    {
        var factory = new MetinArServerFactory();
        HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync(playerId);
        return (factory, client);
    }

    private static async Task<string> StartAsync(HttpClient client, string playerId, string entityId)
    {
        HttpResponseMessage r = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId, worldEntityId = entityId });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.JsonAsync()).GetProperty("encounter").GetProperty("id").GetString()!;
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
                new { playerId = "hunter-1", worldEntityId = "seed-mob-0001", clientPosition = new { latitude = 0.0004, longitude = 0.0004 } });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.JsonAsync();
            Assert.True(json.GetProperty("created").GetBoolean());
            JsonElement e = json.GetProperty("encounter");
            Assert.Equal("hunter-1", e.GetProperty("playerId").GetString());
            Assert.Equal("seed-mob-0001", e.GetProperty("worldEntityId").GetString());
            Assert.Equal("in_progress", e.GetProperty("status").GetString());
            Assert.Equal("2026-10-20T00:00:00.000Z", e.GetProperty("startedAt").GetString());
            Assert.Equal(JsonValueKind.Null, e.GetProperty("completedAt").ValueKind);
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
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = "seed-mob-9999" });

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
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = entityId });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("WORLD_ENTITY_INACTIVE", (await response.JsonAsync()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Start_UnknownPlayer_Returns404()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "ghost-player", worldEntityId = "seed-mob-0001" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PLAYER_NOT_FOUND", (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("""{"worldEntityId":"seed-mob-0001"}""", "playerId")]
    [InlineData("""{"playerId":"hunter-1"}""", "worldEntityId")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0,"longitude":200}}""", "clientPosition.longitude")]
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
            string first = await StartAsync(client, "hunter-1", "seed-mob-0001");

            HttpResponseMessage again = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = "seed-mob-0001" });
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var againJson = await again.JsonAsync();
            Assert.False(againJson.GetProperty("created").GetBoolean());
            Assert.Equal(first, againJson.GetProperty("encounter").GetProperty("id").GetString());

            (await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = first, playerId = "hunter-1" })).EnsureSuccessStatusCode();
            string second = await StartAsync(client, "hunter-1", "seed-mob-0001");
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
            string id = await StartAsync(client, "hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(42));

            var first = await (await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = id, playerId = "hunter-1" })).JsonAsync();
            Assert.True(first.GetProperty("firstCompletion").GetBoolean());
            Assert.Equal("completed", first.GetProperty("encounter").GetProperty("status").GetString());
            Assert.Equal("2026-10-20T00:00:42.000Z", first.GetProperty("encounter").GetProperty("completedAt").GetString());

            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            HttpResponseMessage secondResponse = await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = id, playerId = "hunter-1" });
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var second = await secondResponse.JsonAsync();
            Assert.False(second.GetProperty("firstCompletion").GetBoolean());
            // The original completion timestamp is preserved; a retry never re-completes.
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
            string id = await StartAsync(client, "hunter-1", "seed-mob-0002");

            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = id, playerId = "hunter-1" })));

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
            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = "0000000000000000", playerId = "hunter-1" });

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
            string id = await StartAsync(client, "hunter-1", "seed-mob-0001");

            HttpResponseMessage response = await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = id, playerId = "hunter-2" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("ENCOUNTER_PLAYER_MISMATCH", (await response.JsonAsync()).GetProperty("code").GetString());

            var owner = await (await client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId = id, playerId = "hunter-1" })).JsonAsync();
            Assert.True(owner.GetProperty("firstCompletion").GetBoolean());
        }
    }
}
