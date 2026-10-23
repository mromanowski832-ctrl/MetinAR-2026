using MetinAR.Map;
using MetinAR.Map.Osm;
using Xunit;

namespace MapCore.Tests;

internal sealed class FakeTransport : ITileHttpTransport
{
    public readonly Queue<Func<HttpTileResponse>> Responses = new();
    public readonly List<(string Url, Dictionary<string, string> Headers)> Requests = new();
    public TimeSpan? LastTimeout;
    public int Delay;
    private int _active;
    public int PeakActive;

    public async Task<HttpTileResponse> GetAsync(string url, IReadOnlyDictionary<string, string> requestHeaders, TimeSpan timeout, CancellationToken ct)
    {
        lock (Requests) Requests.Add((url, new Dictionary<string, string>(requestHeaders, StringComparer.OrdinalIgnoreCase)));
        LastTimeout = timeout;
        int a = Interlocked.Increment(ref _active);
        lock (Requests) PeakActive = Math.Max(PeakActive, a);
        try
        {
            if (Delay > 0) await Task.Delay(Delay, ct);
            ct.ThrowIfCancellationRequested();
            Func<HttpTileResponse> next;
            lock (Responses) next = Responses.Count > 0 ? Responses.Dequeue() : () => Ok(new byte[] { 1 });
            return next();
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public static HttpTileResponse Ok(byte[] body, params (string, string)[] headers)
    {
        var r = new HttpTileResponse { StatusCode = 200, Body = body };
        foreach (var (k, v) in headers) r.Headers[k] = v;
        return r;
    }

    public static HttpTileResponse Status(int code) => new HttpTileResponse { StatusCode = code };
}

public class FetcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "metinar-fetch-test-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
    private readonly List<TimeSpan> _delays = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private CachedHttpFetcher Make(FakeTransport t, MapHttpOptions o = null) =>
        new CachedHttpFetcher(t, new DiskTileCache(_dir, 1 << 20, () => _now), o ?? new MapHttpOptions(), () => _now,
            (d, ct) => { _delays.Add(d); return Task.CompletedTask; });

    [Fact]
    public async Task SendsUniqueUserAgent_AndCachesFreshHits()
    {
        var t = new FakeTransport();
        t.Responses.Enqueue(() => FakeTransport.Ok(new byte[] { 7, 7 }, ("Cache-Control", "max-age=60")));
        var f = Make(t);
        var r1 = await f.GetAsync("https://example.test/1", "k1", CancellationToken.None);
        var r2 = await f.GetAsync("https://example.test/1", "k1", CancellationToken.None);
        Assert.Equal(TileFetchStatus.Ok, r1.Status);
        Assert.False(r1.FromCache);
        Assert.Equal(2, r1.NetworkBytes);
        Assert.True(r2.FromCache);
        Assert.Single(t.Requests);
        Assert.StartsWith("MetinAR/", t.Requests[0].Headers["User-Agent"]);
        Assert.Contains("github.com/mromanowski832-ctrl/MetinAR-2026", t.Requests[0].Headers["User-Agent"]);
        Assert.Equal(TimeSpan.FromSeconds(15), t.LastTimeout);
    }

    [Fact]
    public async Task StaleEntry_RevalidatesWithEtag_And304ReusesBody()
    {
        var t = new FakeTransport();
        t.Responses.Enqueue(() => FakeTransport.Ok(new byte[] { 1, 2, 3 }, ("Cache-Control", "max-age=60"), ("ETag", "\"v1\""), ("Last-Modified", "Sun, 11 Oct 2026 13:59:18 GMT")));
        t.Responses.Enqueue(() => { var r = FakeTransport.Status(304); r.Headers["Cache-Control"] = "max-age=60"; return r; });
        var f = Make(t);
        await f.GetAsync("u", "k", CancellationToken.None);
        _now = _now.AddMinutes(5);
        var r2 = await f.GetAsync("u", "k", CancellationToken.None);
        Assert.Equal(2, t.Requests.Count);
        Assert.Equal("\"v1\"", t.Requests[1].Headers["If-None-Match"]);
        Assert.Equal("Sun, 11 Oct 2026 13:59:18 GMT", t.Requests[1].Headers["If-Modified-Since"]);
        Assert.True(r2.FromCache);
        Assert.Equal(new byte[] { 1, 2, 3 }, r2.Data);
        Assert.Equal(0, r2.NetworkBytes);
        // Refreshed: a third call within 60 s is a pure cache hit.
        await f.GetAsync("u", "k", CancellationToken.None);
        Assert.Equal(2, t.Requests.Count);
    }

    [Fact]
    public async Task RetriesTransientErrors_WithExponentialBackoff()
    {
        var t = new FakeTransport();
        t.Responses.Enqueue(() => new HttpTileResponse { StatusCode = 0, TimedOut = true });
        t.Responses.Enqueue(() => FakeTransport.Status(503));
        t.Responses.Enqueue(() => FakeTransport.Ok(new byte[] { 9 }));
        var r = await Make(t).GetAsync("u", "k", CancellationToken.None);
        Assert.Equal(TileFetchStatus.Ok, r.Status);
        Assert.Equal(3, r.Attempts);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1) }, _delays);
    }

    [Fact]
    public async Task NonRetryable4xx_FailsOnce_404IsEmpty()
    {
        var t = new FakeTransport();
        t.Responses.Enqueue(() => FakeTransport.Status(403));
        var r = await Make(t).GetAsync("u", "k", CancellationToken.None);
        Assert.Equal(TileFetchStatus.Failed, r.Status);
        Assert.Equal(1, r.Attempts);
        Assert.Contains("403", r.Error);

        t.Responses.Enqueue(() => FakeTransport.Status(404));
        var e = await Make(t).GetAsync("u2", "k2", CancellationToken.None);
        Assert.Equal(TileFetchStatus.Empty, e.Status);
    }

    [Fact]
    public async Task NetworkDown_ServesStaleCache()
    {
        var t = new FakeTransport();
        t.Responses.Enqueue(() => FakeTransport.Ok(new byte[] { 5 }, ("Cache-Control", "max-age=1")));
        for (int i = 0; i < 3; i++) t.Responses.Enqueue(() => new HttpTileResponse { StatusCode = 0, Error = "offline" });
        var f = Make(t);
        await f.GetAsync("u", "k", CancellationToken.None);
        _now = _now.AddHours(1);
        var r = await f.GetAsync("u", "k", CancellationToken.None);
        Assert.Equal(TileFetchStatus.Ok, r.Status);
        Assert.True(r.Stale);
        Assert.Equal(new byte[] { 5 }, r.Data);
    }

    [Fact]
    public async Task Cancellation_StopsRequest()
    {
        var t = new FakeTransport { Delay = 5000 };
        using var cts = new CancellationTokenSource(50);
        var f = new CachedHttpFetcher(t, null, new MapHttpOptions());
        var r = await f.GetAsync("u", null, cts.Token);
        Assert.Equal(TileFetchStatus.Cancelled, r.Status);
    }

    [Fact]
    public async Task ParallelRequests_AreBoundedToFour()
    {
        var t = new FakeTransport { Delay = 30 };
        var f = new CachedHttpFetcher(t, null, new MapHttpOptions());
        var tasks = Enumerable.Range(0, 12).Select(i => f.GetAsync("u" + i, null, CancellationToken.None)).ToArray();
        await Task.WhenAll(tasks);
        Assert.True(t.PeakActive <= 4, "peak=" + t.PeakActive);
        Assert.True(f.PeakInFlight <= 4);
    }

    [Fact]
    public async Task OpenFreeMapSource_ReadsTileJson_BuildsUrls_AndAttribution()
    {
        var t = new FakeTransport();
        string json = "{\"tilejson\":\"3.0.0\",\"tiles\":[\"https://tiles.example.test/planet/20261011_080001_pt/{z}/{x}/{y}.pbf\"],"
            + "\"attribution\":\"<a href=\\\"https://openfreemap.org\\\">OpenFreeMap</a> <a href=\\\"https://www.openmaptiles.org/\\\">&copy; OpenMapTiles</a> Data from <a href=\\\"https://www.openstreetmap.org/copyright\\\">OpenStreetMap</a>\","
            + "\"maxzoom\":14,\"vector_layers\":[{\"id\":\"water\",\"fields\":{\"class\":\"String\"}}],\"bounds\":[-180,-85.05,180,85.05]}";
        t.Responses.Enqueue(() => FakeTransport.Ok(System.Text.Encoding.UTF8.GetBytes(json), ("Cache-Control", "max-age=3600")));
        t.Responses.Enqueue(() => FakeTransport.Ok(new byte[] { 1 }, ("Cache-Control", "max-age=3600")));
        var src = new OpenFreeMapTileSource(Make(t), "https://tiles.example.test/planet");
        var r = await src.FetchAsync(new TileId(14, 9099, 5552), CancellationToken.None);
        Assert.Equal(TileFetchStatus.Ok, r.Status);
        Assert.Equal(14, src.SourceMaxZoom);
        Assert.Equal("https://tiles.example.test/planet", t.Requests[0].Url);
        Assert.Equal("https://tiles.example.test/planet/20261011_080001_pt/14/9099/5552.pbf", t.Requests[1].Url);
        Assert.Equal(new[] { "OpenFreeMap", "© OpenMapTiles", "Data from", "OpenStreetMap" }, src.Attribution);
        Assert.Throws<ArgumentOutOfRangeException>(() => { src.FetchAsync(new TileId(15, 0, 0), CancellationToken.None).GetAwaiter().GetResult(); });
    }

    [Fact]
    public void TileJson_RejectsMissingTemplate_AndPlainHttp()
    {
        var src = new OpenFreeMapTileSource(new CachedHttpFetcher(new FakeTransport(), null, new MapHttpOptions()));
        Assert.Throws<FormatException>(() => src.ApplyTileJson("{\"tiles\":[]}"));
        Assert.Throws<FormatException>(() => src.ApplyTileJson("{\"tiles\":[\"http://x/{z}/{x}/{y}.pbf\"]}"));
        Assert.Throws<FormatException>(() => src.ApplyTileJson("{\"tiles\":"));
    }

    [Fact]
    public void MiniJson_ParsesNestedValues()
    {
        var o = (Dictionary<string, object>)MiniJson.Parse("{\"a\":[1,2.5,-3e2],\"b\":{\"c\":true,\"d\":null},\"e\":\"x\\u00e9\\\"\"}");
        Assert.Equal(new object[] { 1.0, 2.5, -300.0 }, ((List<object>)o["a"]).ToArray());
        Assert.Equal(true, ((Dictionary<string, object>)o["b"])["c"]);
        Assert.Null(((Dictionary<string, object>)o["b"])["d"]);
        Assert.Equal("xé\"", o["e"]);
    }
}
