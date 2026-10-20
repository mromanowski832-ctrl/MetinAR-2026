using System.Net;
using System.Text.Json;
using Xunit;

namespace MetinAR.Server.Tests;

public class WorldNearbyTests
{
    private static string[] Ids(JsonElement json) =>
        json.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task Nearby_ReturnsOnlyActiveEntitiesInsideRadius_SortedByDistance()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=300");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.JsonAsync();
        // seed-mob-0003 (inactive flag), seed-mob-0004 (despawned) and seed-mob-0005 (~7.9 km) are excluded.
        Assert.Equal(new[] { "seed-mob-0001", "seed-npc-0001", "seed-mob-0002" }, Ids(json));
        Assert.Equal(3, json.GetProperty("totalInRadius").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextOffset").ValueKind);

        JsonElement first = json.GetProperty("entities")[0];
        Assert.Equal("mob", first.GetProperty("type").GetString());
        Assert.Equal("prototype_mob_a", first.GetProperty("templateId").GetString());
        Assert.True(first.GetProperty("aggressive").GetBoolean());
        Assert.True(first.GetProperty("active").GetBoolean());
        Assert.Equal("2026-01-01T00:00:00.000Z", first.GetProperty("spawnedAt").GetString());
        Assert.InRange(first.GetProperty("distanceMeters").GetDouble(), 78.0, 79.5);

        double[] distances = json.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("distanceMeters").GetDouble()).ToArray();
        Assert.Equal(distances.OrderBy(d => d).ToArray(), distances);
    }

    [Fact]
    public async Task Nearby_SmallRadiusExcludesFartherEntities()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        var json = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=100")).JsonAsync();

        Assert.Equal(new[] { "seed-mob-0001" }, Ids(json));
    }

    [Fact]
    public async Task Nearby_EntityInsideSpawnWindowIsReturned_AndDisappearsAfterDespawn()
    {
        using var factory = new MetinArServerFactory(now: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using HttpClient client = factory.CreateClient();

        var before = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=50")).JsonAsync();
        Assert.Equal(new[] { "seed-mob-0004" }, Ids(before));

        factory.Clock.Set(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)); // exactly despawnAt (exclusive)
        var after = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=50")).JsonAsync();
        Assert.Empty(Ids(after));
    }

    [Fact]
    public async Task Nearby_PagingIsStableAndComplete()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        var page1 = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=300&limit=2")).JsonAsync();
        Assert.Equal(new[] { "seed-mob-0001", "seed-npc-0001" }, Ids(page1));
        Assert.Equal(2, page1.GetProperty("nextOffset").GetInt32());

        var page2 = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0&radiusMeters=300&limit=2&offset=2")).JsonAsync();
        Assert.Equal(new[] { "seed-mob-0002" }, Ids(page2));
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextOffset").ValueKind);
        Assert.Equal(3, page2.GetProperty("totalInRadius").GetInt32());
    }

    [Fact]
    public async Task Nearby_DefaultsRadiusWhenOmitted()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        var json = await (await client.GetAsync("/api/v1/world/nearby?lat=0&lon=0")).JsonAsync();

        Assert.Equal(250, json.GetProperty("radiusMeters").GetDouble());
        Assert.Equal(new[] { "seed-mob-0001", "seed-npc-0001", "seed-mob-0002" }, Ids(json));
    }

    [Theory]
    [InlineData("lat=0&lon=0&radiusMeters=1001", "radiusMeters")]
    [InlineData("lat=0&lon=0&radiusMeters=0", "radiusMeters")]
    [InlineData("lat=91&lon=0", "query.latitude")]
    [InlineData("lat=0&lon=-181", "query.longitude")]
    [InlineData("lon=0", "lat")]
    [InlineData("lat=0", "lon")]
    [InlineData("lat=0&lon=0&limit=101", "limit")]
    [InlineData("lat=0&lon=0&limit=0", "limit")]
    [InlineData("lat=0&lon=0&offset=-1", "offset")]
    public async Task Nearby_RejectsOutOfRangeQueries(string query, string field)
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/world/nearby?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("VALIDATION_FAILED", json.GetProperty("code").GetString());
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out _), $"expected error for {field}");
    }

    [Fact]
    public async Task Nearby_NonNumericQueryIsMalformedRequest()
    {
        using var factory = new MetinArServerFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/world/nearby?lat=abc&lon=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("MALFORMED_REQUEST", json.GetProperty("code").GetString());
        Assert.Equal("Parameter 'lat' has an invalid format.", json.GetProperty("detail").GetString());
    }
}
