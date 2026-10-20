using System.Net;
using System.Text.Json;
using Xunit;

namespace MetinAR.Server.Tests;

public class PlayerTests
{
    [Fact]
    public async Task GetUnknownPlayer_Returns404ProblemWithCode()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/player/nobody-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.JsonAsync();
        Assert.Equal("PLAYER_NOT_FOUND", json.GetProperty("code").GetString());
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Put_CreatesThenUpdates_ServerOwnedStatsUseDefaults()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage created = await client.PutJsonAsync("/api/v1/player/player-1", new { displayName = "  Alpha  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/v1/player/player-1", created.Headers.Location?.OriginalString);
        var c = await created.JsonAsync();
        Assert.Equal("Alpha", c.GetProperty("displayName").GetString());
        Assert.Equal(1, c.GetProperty("level").GetInt32());
        Assert.Equal(100, c.GetProperty("hp").GetProperty("current").GetInt32());
        Assert.Equal(100, c.GetProperty("hp").GetProperty("max").GetInt32());
        Assert.Equal(0, c.GetProperty("experience").GetInt64());
        Assert.Equal(JsonValueKind.Null, c.GetProperty("lastKnownPosition").ValueKind);
        Assert.Equal("2026-10-20T00:00:00.000Z", c.GetProperty("createdAt").GetString());

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        HttpResponseMessage updated = await client.PutJsonAsync("/api/v1/player/player-1", new { displayName = "Beta" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var u = await updated.JsonAsync();
        Assert.Equal("Beta", u.GetProperty("displayName").GetString());
        Assert.Equal("2026-10-20T00:00:00.000Z", u.GetProperty("createdAt").GetString());
        Assert.Equal("2026-10-20T00:05:00.000Z", u.GetProperty("updatedAt").GetString());

        var fetched = await (await client.GetAsync("/api/v1/player/player-1")).JsonAsync();
        Assert.Equal("Beta", fetched.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Put_StoresOnlyCoarsePosition_AndKeepsItWhenOmitted()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        var first = await (await client.PutJsonAsync("/api/v1/player/player-2",
            new { displayName = "Geo", lastKnownPosition = new { latitude = 0.0123456, longitude = -0.0006789 } })).JsonAsync();
        JsonElement pos = first.GetProperty("lastKnownPosition");
        Assert.Equal(0.012, pos.GetProperty("latitude").GetDouble());
        Assert.Equal(-0.001, pos.GetProperty("longitude").GetDouble());
        Assert.Equal("2026-10-20T00:00:00.000Z", pos.GetProperty("reportedAt").GetString());

        var second = await (await client.PutJsonAsync("/api/v1/player/player-2", new { displayName = "Geo2" })).JsonAsync();
        Assert.Equal(0.012, second.GetProperty("lastKnownPosition").GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task Put_RejectsAttemptToWriteServerOwnedField()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Put, "/api/v1/player/player-3",
            """{"displayName":"Cheater","level":99}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("MALFORMED_REQUEST", json.GetProperty("code").GetString());
        Assert.Equal("Unknown field 'level'. Only documented fields are accepted.", json.GetProperty("detail").GetString());
        Assert.DoesNotContain("MetinAR.Server", json.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/player/player-3")).StatusCode);
    }

    [Theory]
    [InlineData("ab", """{"displayName":"x"}""", "playerId")]
    [InlineData("bad%20id", """{"displayName":"x"}""", "playerId")]
    [InlineData("player-4", """{"displayName":"   "}""", "displayName")]
    [InlineData("player-4", """{}""", "displayName")]
    [InlineData("player-4", """{"displayName":"0123456789012345678901234567890123"}""", "displayName")]
    [InlineData("player-4", """{"displayName":"ok","lastKnownPosition":{"latitude":95,"longitude":0}}""", "lastKnownPosition.latitude")]
    public async Task Put_ValidatesInput(string playerId, string body, string field)
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Put, "/api/v1/player/" + playerId, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("VALIDATION_FAILED", json.GetProperty("code").GetString());
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out _), $"expected error for {field}: {json}");
    }

    [Fact]
    public async Task Put_MalformedJsonIsRejected()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Put, "/api/v1/player/player-5", "{\"displayName\":");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MALFORMED_REQUEST", (await response.JsonAsync()).GetProperty("code").GetString());
    }
}
