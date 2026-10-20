using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class HttpCacheTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Fallback = TimeSpan.FromDays(7);
    private static readonly TimeSpan Max = TimeSpan.FromDays(30);

    private static Dictionary<string, string> H(params (string, string)[] kv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void MaxAge_MinusAge_IsCappedByMaxTtl()
    {
        var d = HttpCachePolicy.Evaluate(H(("Cache-Control", "max-age=3600, public"), ("Age", "600")), Now, Fallback, Max);
        Assert.True(d.Store);
        Assert.Equal(Now.AddSeconds(3000), d.FreshUntil);

        var longLived = HttpCachePolicy.Evaluate(H(("Cache-Control", "max-age=315360000, public")), Now, Fallback, Max);
        Assert.Equal(Now + Max, longLived.FreshUntil);
    }

    [Fact]
    public void NoStore_DoesNotStore_NoCache_StoresButIsStale()
    {
        Assert.False(HttpCachePolicy.Evaluate(H(("Cache-Control", "no-store")), Now, Fallback, Max).Store);
        var nc = HttpCachePolicy.Evaluate(H(("Cache-Control", "no-cache"), ("ETag", "W/\"x\"")), Now, Fallback, Max);
        Assert.True(nc.Store);
        Assert.Equal(Now, nc.FreshUntil);
        Assert.Equal("W/\"x\"", nc.ETag);
    }

    [Fact]
    public void Expires_RelativeToDate_AndFallbackWithoutHeaders()
    {
        var d = HttpCachePolicy.Evaluate(
            H(("Date", "Tue, 20 Oct 2026 02:00:00 GMT"), ("Expires", "Tue, 20 Oct 2026 04:00:00 GMT"), ("Last-Modified", "Sun, 11 Oct 2026 13:59:18 GMT")),
            Now, Fallback, Max);
        Assert.Equal(Now.AddHours(2), d.FreshUntil);
        Assert.Equal("Sun, 11 Oct 2026 13:59:18 GMT", d.LastModified);

        Assert.Equal(Now + Fallback, HttpCachePolicy.Evaluate(H(), Now, Fallback, Max).FreshUntil);
    }

    [Fact]
    public void DiskCache_RoundTrips_AndEvictsLeastRecentlyUsed()
    {
        string dir = Path.Combine(Path.GetTempPath(), "metinar-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new DiskTileCache(dir, 250);
            cache.Put("src/14/1/1", new TileCacheEntry { Data = new byte[100], FreshUntil = Now, ETag = "a" });
            cache.Put("src/14/1/2", new TileCacheEntry { Data = new byte[100], FreshUntil = Now });
            Assert.True(cache.TryGet("src/14/1/1", out var e1)); // touch 1 → 2 is now LRU
            Assert.Equal("a", e1.ETag);
            cache.Put("src/14/1/3", new TileCacheEntry { Data = new byte[100], FreshUntil = Now });
            Assert.True(cache.TotalBytes <= 250);
            Assert.False(cache.TryGet("src/14/1/2", out _));
            Assert.True(cache.TryGet("src/14/1/1", out _));
            Assert.True(cache.TryGet("src/14/1/3", out _));

            // Re-open from disk: index rebuilt, bound still enforced.
            var reopened = new DiskTileCache(dir, 150);
            Assert.True(reopened.TotalBytes <= 150);
            Assert.Equal(1, reopened.Count);

            // Oversized entry is never stored.
            reopened.Put("big", new TileCacheEntry { Data = new byte[1000], FreshUntil = Now });
            Assert.False(reopened.TryGet("big", out _));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
