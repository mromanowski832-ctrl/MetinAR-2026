using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MetinAR.Server.Tests;

/// <summary>Deterministic, manually advanced clock.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset value) => _now = value;
}

/// <summary>
/// Hosts the real application in-process (TestServer, no sockets, no external network) against a
/// throw-away SQLite file. Reusing the same <see cref="DatabasePath"/> with a new factory simulates a restart.
/// </summary>
public sealed class MetinArServerFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly IReadOnlyDictionary<string, string> _settings;

    public MetinArServerFactory(string? databasePath = null, DateTimeOffset? now = null, IReadOnlyDictionary<string, string>? settings = null)
    {
        DatabasePath = databasePath ?? NewDatabasePath();
        Clock = new ManualTimeProvider(now ?? DefaultNow);
        _settings = settings ?? new Dictionary<string, string>();
    }

    /// <summary>Every log line the app writes (after the normal configuration filters).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    public string DatabasePath { get; }

    public ManualTimeProvider Clock { get; }

    public static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), "metinar-tests", Guid.NewGuid().ToString("N"), "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:MetinAR", $"Data Source={DatabasePath}");
        foreach ((string key, string value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Release pooled handles so the temp file can be reopened/deleted by the next "process".
        SqliteConnection.ClearAllPools();
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Capture(string category, System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

public static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body);

    public static Task<HttpResponseMessage> SendRawJsonAsync(this HttpClient client, HttpMethod method, string url, string json) =>
        client.SendAsync(new HttpRequestMessage(method, url) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

    public static async Task<HttpClient> WithPlayerAsync(this HttpClient client, string playerId)
    {
        HttpResponseMessage r = await client.PutJsonAsync($"/api/v1/player/{playerId}", new { displayName = "Test " + playerId });
        r.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>A synthetic fix ~2 m from seed-mob-0001 (0.0005, 0.0005) with 5 m accuracy.</summary>
    public static object NearFix(double latitude = 0.0005, double longitude = 0.00048, double accuracy = 5) =>
        new { latitude, longitude, horizontalAccuracyMeters = accuracy };

    /// <summary>A fix placed exactly on the given entity coordinates.</summary>
    public static readonly Dictionary<string, (double Lat, double Lon)> SeedPositions = new()
    {
        ["seed-mob-0001"] = (0.0005, 0.0005),
        ["seed-mob-0002"] = (0.0020, -0.0010),
        ["seed-npc-0001"] = (0.0000, 0.0010),
        ["seed-mob-0003"] = (0.0003, 0.0000),
        ["seed-mob-0004"] = (0.0000, -0.0003),
        ["seed-mob-0006"] = (-0.0200, 0.0002),
        ["seed-npc-0002"] = (-0.0200, -0.0002),
    };

    public static object FixAt(string entityId, double accuracy = 5) =>
        SeedPositions.TryGetValue(entityId, out var p) ? NearFix(p.Lat, p.Lon, accuracy) : NearFix();

    /// <summary>Start an encounter standing on the entity; returns (encounterId, nonce).</summary>
    public static async Task<(string Id, string Nonce)> StartAtAsync(this HttpClient client, string playerId, string entityId)
    {
        HttpResponseMessage r = await client.PostJsonAsync("/api/v1/encounters/start", new { playerId, worldEntityId = entityId, clientPosition = FixAt(entityId) });
        string text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"start failed {(int)r.StatusCode}: {text}");
        }

        JsonElement json = JsonDocument.Parse(text).RootElement;
        return (json.GetProperty("encounter").GetProperty("id").GetString()!, json.GetProperty("nonce").GetString()!);
    }

    public static Task<HttpResponseMessage> CompleteAsync(this HttpClient client, string encounterId, string playerId, string nonce) =>
        client.PostJsonAsync("/api/v1/encounters/complete", new { encounterId, playerId, nonce });
}
