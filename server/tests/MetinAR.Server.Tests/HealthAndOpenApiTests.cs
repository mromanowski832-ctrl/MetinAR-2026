using System.Net;
using Xunit;

namespace MetinAR.Server.Tests;

public class HealthAndOpenApiTests
{
    [Fact]
    public async Task Health_ReturnsOkWithSchemaVersionAndUtcTime()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal("v1", json.GetProperty("apiVersion").GetString());
        Assert.Equal("0003_encounter_authority_and_spawn_schedules", json.GetProperty("schemaVersion").GetString());
        Assert.Equal("2026-10-20T00:00:00.000Z", json.GetProperty("serverTime").GetString());
    }

    [Fact]
    public async Task OpenApi_DocumentListsAllPrototypeEndpoints()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await response.JsonAsync()).GetProperty("paths");
        foreach (string path in new[] { "/health", "/api/v1/player/{playerId}", "/api/v1/world/nearby", "/api/v1/encounters/start", "/api/v1/encounters/complete" })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI is missing {path}");
        }

        HttpResponseMessage ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemJsonWithCode()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("NOT_FOUND", (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task WrongMethod_ReturnsProblemJsonWithCode()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync("/api/v1/player/someone-1");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("METHOD_NOT_ALLOWED", (await response.JsonAsync()).GetProperty("code").GetString());
    }
}
