using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace MetinAR.Server.Tests;

/// <summary>METINAR-012: proximity authority, replay protection / TTL, audit trail, log privacy.</summary>
public class ServerAuthorityTests
{
    // seed-mob-0001 sits at (0.0005, 0.0005). One meter of latitude is 1 / 111195 degrees.
    private const double EntityLat = 0.0005;
    private const double EntityLon = 0.0005;
    private const double DegreesPerMeter = 1.0 / 111_195.0;

    private static object FixNorthOfEntity(double meters, double accuracy, DateTimeOffset? capturedAt = null) =>
        capturedAt is null
            ? new { latitude = EntityLat + (meters * DegreesPerMeter), longitude = EntityLon, horizontalAccuracyMeters = accuracy }
            : new { latitude = EntityLat + (meters * DegreesPerMeter), longitude = EntityLon, horizontalAccuracyMeters = accuracy, capturedAt = capturedAt.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) };

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, object fix, string entity = "seed-mob-0001") =>
        await client.PostJsonAsync("/api/v1/encounters/start", new { playerId = "hunter-1", worldEntityId = entity, clientPosition = fix });

    private static async Task<(MetinArServerFactory, HttpClient)> ArrangeAsync(IReadOnlyDictionary<string, string>? settings = null)
    {
        var factory = new MetinArServerFactory(settings: settings);
        HttpClient client = factory.CreateClient();
        await client.WithPlayerAsync("hunter-1");
        return (factory, client);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await response.JsonAsync()).GetProperty("code").GetString());
    }

    // ------------------------------------------------------------------ proximity

    [Theory]
    [InlineData(0, 5)]
    [InlineData(35, 0)]    // inside the 40 m base distance even with a perfect fix
    [InlineData(55, 20)]   // 40 m + min(20, cap 20) = 60 m
    [InlineData(59, 30)]   // accuracy 30 is capped to 20 m of allowance -> 60 m
    public async Task Start_NearEntity_IsAllowed(double meters, double accuracy)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await StartAsync(client, FixNorthOfEntity(meters, accuracy));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(55, 5)]    // 40 + 5 = 45 m allowed
    [InlineData(70, 50)]   // worst accepted accuracy still buys only 20 m -> 60 m allowed
    [InlineData(500, 10)]  // clearly distant
    [InlineData(250_000, 10)] // impossible (different region)
    public async Task Start_DistantFromEntity_IsRejected(double meters, double accuracy)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await StartAsync(client, FixNorthOfEntity(meters, accuracy));
            await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "ENCOUNTER_TOO_FAR");
            var json = await response.JsonAsync();
            Assert.InRange(json.GetProperty("distanceMeters").GetDouble(), meters - 1, meters + 1);
            Assert.Equal(40 + Math.Min(accuracy, 20), json.GetProperty("allowedMeters").GetDouble());
        }
    }

    [Fact]
    public async Task Start_PoorAccuracy_IsRejected_EvenOnTopOfEntity()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            await AssertProblemAsync(await StartAsync(client, FixNorthOfEntity(0, 50.5)), HttpStatusCode.UnprocessableEntity, "GPS_ACCURACY_INSUFFICIENT");
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(client, FixNorthOfEntity(0, 50))).StatusCode);
        }
    }

    [Theory]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001"}""", "clientPosition")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0.0005,"longitude":0.0005}}""", "clientPosition.horizontalAccuracyMeters")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0.0005,"longitude":0.0005,"horizontalAccuracyMeters":-1}}""", "clientPosition.horizontalAccuracyMeters")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"longitude":0.0005,"horizontalAccuracyMeters":5}}""", "clientPosition.latitude")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":90.5,"longitude":0.0005,"horizontalAccuracyMeters":5}}""", "clientPosition.latitude")]
    [InlineData("""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0.0005,"longitude":-180.5,"horizontalAccuracyMeters":5}}""", "clientPosition.longitude")]
    public async Task Start_InvalidCoordinates_AreRejected(string body, string field)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Post, "/api/v1/encounters/start", body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
            Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _), field);
        }
    }

    [Theory]
    [InlineData("""{"latitude":"north","longitude":0.0005,"horizontalAccuracyMeters":5}""")]
    [InlineData("""{"latitude":0.0005,"longitude":0.0005,"horizontalAccuracyMeters":5,"altitude":100}""")]
    [InlineData("""{"latitude":0.0005,"longitude":0.0005,"horizontalAccuracyMeters":5,"capturedAt":"yesterday"}""")]
    public async Task Start_MalformedFix_IsRejected(string fix)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Post, "/api/v1/encounters/start",
                $$"""{"playerId":"hunter-1","worldEntityId":"seed-mob-0001","clientPosition":{{fix}}}""");
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "MALFORMED_REQUEST");
        }
    }

    [Theory]
    [InlineData(-31, false)]  // older than MaxFixAgeSeconds (30)
    [InlineData(10, false)]   // more than 5 s in the future
    [InlineData(-30, true)]
    [InlineData(4, true)]
    public async Task Start_FixTimestamp_MustBeCurrent(int offsetSeconds, bool allowed)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await StartAsync(client, FixNorthOfEntity(0, 5, MetinArServerFactory.DefaultNow.AddSeconds(offsetSeconds)));
            if (allowed)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            else
            {
                await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "GPS_FIX_STALE");
            }
        }
    }

    [Fact]
    public async Task Start_RadiusIsConfigurable()
    {
        var (factory, client) = await ArrangeAsync(new Dictionary<string, string>
        {
            ["EncounterRules:MaxStartDistanceMeters"] = "100",
            ["EncounterRules:AccuracyAllowanceCapMeters"] = "0",
        });
        using (factory)
        using (client)
        {
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(client, FixNorthOfEntity(95, 10))).StatusCode);
            await AssertProblemAsync(await StartAsync(client, FixNorthOfEntity(105, 10), "seed-mob-0002"), HttpStatusCode.UnprocessableEntity, "ENCOUNTER_TOO_FAR");
        }
    }

    [Fact]
    public void InvalidEncounterRules_FailAtStartup()
    {
        using var factory = new MetinArServerFactory(settings: new Dictionary<string, string> { ["EncounterRules:EncounterTtlSeconds"] = "0" });
        Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());
    }

    // ------------------------------------------------------------------ TTL / replay

    [Fact]
    public async Task Complete_AfterTtl_IsRejected_AndSessionBecomesExpired()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(600)); // exactly expiresAt (exclusive)

            HttpResponseMessage late = await client.CompleteAsync(id, "hunter-1", nonce);
            await AssertProblemAsync(late, HttpStatusCode.Gone, "ENCOUNTER_EXPIRED");

            // Repeating the late call stays rejected; the session never turns into a completion.
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            await AssertProblemAsync(await client.CompleteAsync(id, "hunter-1", nonce), HttpStatusCode.Gone, "ENCOUNTER_EXPIRED");

            await using var db = new SqliteConnection($"Data Source={factory.DatabasePath}");
            await db.OpenAsync();
            await using SqliteCommand q = db.CreateCommand();
            q.CommandText = "SELECT status, expired_at, completed_at FROM encounter_sessions WHERE id = $id;";
            q.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader r = await q.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("expired", r.GetString(0));
            Assert.Equal("2026-10-20T00:10:00.000Z", r.GetString(1));
            Assert.True(r.IsDBNull(2));
        }
    }

    [Fact]
    public async Task Complete_JustBeforeTtl_Succeeds()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(599.999));

            var json = await (await client.CompleteAsync(id, "hunter-1", nonce)).JsonAsync();
            Assert.True(json.GetProperty("firstCompletion").GetBoolean());
        }
    }

    [Fact]
    public async Task Start_AfterTtl_DoesNotReuseExpiredSession()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string first, string firstNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(601));

            HttpResponseMessage again = await client.PostJsonAsync("/api/v1/encounters/start",
                new { playerId = "hunter-1", worldEntityId = "seed-mob-0001", clientPosition = HttpExtensions.NearFix() });
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
            var json = await again.JsonAsync();
            Assert.True(json.GetProperty("created").GetBoolean());
            Assert.NotEqual(first, json.GetProperty("encounter").GetProperty("id").GetString());

            await AssertProblemAsync(await client.CompleteAsync(first, "hunter-1", firstNonce), HttpStatusCode.Gone, "ENCOUNTER_EXPIRED");
        }
    }

    [Fact]
    public async Task TtlIsConfigurable()
    {
        var (factory, client) = await ArrangeAsync(new Dictionary<string, string> { ["EncounterRules:EncounterTtlSeconds"] = "60" });
        using (factory)
        using (client)
        {
            HttpResponseMessage start = await StartAsync(client, HttpExtensions.NearFix());
            Assert.Equal("2026-10-20T00:01:00.000Z", (await start.JsonAsync()).GetProperty("encounter").GetProperty("expiresAt").GetString());
        }
    }

    [Fact]
    public async Task Complete_WithWrongOrRotatedNonce_IsRejected_AndDoesNotComplete()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string oldNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            (_, string newNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001"); // reuse rotates the nonce

            await AssertProblemAsync(await client.CompleteAsync(id, "hunter-1", oldNonce), HttpStatusCode.Forbidden, "ENCOUNTER_NONCE_MISMATCH");
            await AssertProblemAsync(await client.CompleteAsync(id, "hunter-1", new string('A', 43)), HttpStatusCode.Forbidden, "ENCOUNTER_NONCE_MISMATCH");

            var ok = await (await client.CompleteAsync(id, "hunter-1", newNonce)).JsonAsync();
            Assert.True(ok.GetProperty("firstCompletion").GetBoolean());
        }
    }

    [Theory]
    [InlineData("""{"encounterId":"abcdef","playerId":"hunter-1"}""")]
    [InlineData("""{"encounterId":"abcdef","playerId":"hunter-1","nonce":"short"}""")]
    public async Task Complete_MissingOrMalformedNonce_IsValidationError(string body)
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            HttpResponseMessage response = await client.SendRawJsonAsync(HttpMethod.Post, "/api/v1/encounters/complete", body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
            Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("nonce", out _));
        }
    }

    [Fact]
    public async Task NonceIsNotStoredInPlaintext()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string id, string nonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            byte[] file = await File.ReadAllBytesAsync(factory.DatabasePath);
            string wal = factory.DatabasePath + "-wal";
            byte[] walBytes = File.Exists(wal) ? await ReadSharedAsync(wal) : [];
            string all = System.Text.Encoding.ASCII.GetString(file) + System.Text.Encoding.ASCII.GetString(walBytes);
            Assert.DoesNotContain(nonce, all, StringComparison.Ordinal);
            Assert.Contains(id, all, StringComparison.Ordinal); // sanity: the session itself is on disk
        }
    }

    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    // ------------------------------------------------------------------ audit trail

    [Fact]
    public async Task StatusTransitions_AreAudited_WithoutClientCoordinates()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            (string done, string doneNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0001");
            factory.Clock.Advance(TimeSpan.FromSeconds(5));
            (await client.CompleteAsync(done, "hunter-1", doneNonce)).EnsureSuccessStatusCode();
            (await client.CompleteAsync(done, "hunter-1", doneNonce)).EnsureSuccessStatusCode(); // idempotent retry: no new row

            (string lapsed, string lapsedNonce) = await client.StartAtAsync("hunter-1", "seed-mob-0002");
            factory.Clock.Advance(TimeSpan.FromSeconds(700));
            Assert.Equal(HttpStatusCode.Gone, (await client.CompleteAsync(lapsed, "hunter-1", lapsedNonce)).StatusCode);

            await using var db = new SqliteConnection($"Data Source={factory.DatabasePath}");
            await db.OpenAsync();
            var rows = new List<string>();
            await using (SqliteCommand q = db.CreateCommand())
            {
                q.CommandText = "SELECT encounter_id, COALESCE(from_status, '-'), to_status, at, reason FROM encounter_transitions ORDER BY at, to_status;";
                await using SqliteDataReader r = await q.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    rows.Add($"{(r.GetString(0) == done ? "done" : "lapsed")} {r.GetString(1)}->{r.GetString(2)} {r.GetString(3)} {r.GetString(4)}");
                }
            }

            string[] expected =
            [
                "done -->in_progress 2026-10-20T00:00:00.000Z started",
                "done in_progress->completed 2026-10-20T00:00:05.000Z completed",
                "lapsed -->in_progress 2026-10-20T00:00:05.000Z started",
                "lapsed in_progress->expired 2026-10-20T00:11:45.000Z ttl_elapsed_on_complete",
            ];
            Assert.Equal(expected, rows.OrderBy(x => x, StringComparer.Ordinal));

            await using SqliteCommand cols = db.CreateCommand();
            cols.CommandText = "SELECT name FROM pragma_table_info('encounter_sessions');";
            var names = new List<string>();
            await using (SqliteDataReader r = await cols.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    names.Add(r.GetString(0));
                }
            }

            Assert.Contains("start_distance_meters", names);
            Assert.DoesNotContain(names, n => n.Contains("lat", StringComparison.OrdinalIgnoreCase) || n.Contains("lon", StringComparison.OrdinalIgnoreCase));
            SqliteConnection.ClearAllPools();
        }
    }

    // ------------------------------------------------------------------ log privacy

    [Fact]
    public async Task Logs_NeverContainClientCoordinates()
    {
        var (factory, client) = await ArrangeAsync();
        using (factory)
        using (client)
        {
            // Distinctive synthetic values so any leak is detectable.
            const string lat = "0.0047317";
            const string lon = "0.0029143";
            await client.GetAsync($"/api/v1/world/nearby?lat={lat}&lon={lon}&radiusMeters=500");
            await client.SendRawJsonAsync(HttpMethod.Post, "/api/v1/encounters/start",
                "{\"playerId\":\"hunter-1\",\"worldEntityId\":\"seed-mob-0001\",\"clientPosition\":{\"latitude\":" + lat + ",\"longitude\":" + lon + ",\"horizontalAccuracyMeters\":7}}");
            await client.SendRawJsonAsync(HttpMethod.Put, "/api/v1/player/hunter-1",
                "{\"displayName\":\"x\",\"lastKnownPosition\":{\"latitude\":" + lat + ",\"longitude\":" + lon + "}}");

            IReadOnlyCollection<string> lines = factory.Logs.Lines;
            Assert.Contains(lines, l => l.Contains("Encounter start rejected: TooFar", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains(lat, StringComparison.Ordinal) || l.Contains(lon, StringComparison.Ordinal)
                || l.Contains("0.0047", StringComparison.Ordinal) || l.Contains("0.0029", StringComparison.Ordinal));
        }
    }
}
