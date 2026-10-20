using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public MetinArServerFactory(string? databasePath = null, DateTimeOffset? now = null)
    {
        DatabasePath = databasePath ?? NewDatabasePath();
        Clock = new ManualTimeProvider(now ?? DefaultNow);
    }

    public string DatabasePath { get; }

    public ManualTimeProvider Clock { get; }

    public static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), "metinar-tests", Guid.NewGuid().ToString("N"), "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:MetinAR", $"Data Source={DatabasePath}");
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
}
