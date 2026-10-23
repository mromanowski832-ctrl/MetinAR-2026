using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetinAR.Map
{
    /// <summary>Retry / timeout / identification settings for map HTTP requests.</summary>
    public sealed class MapHttpOptions
    {
        /// <summary>Unique, stable app identification (required by OSM-community tile policies).</summary>
        public string UserAgent { get; set; } = MapUserAgent.Default;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Total attempts per request (1 = no retry).</summary>
        public int MaxAttempts { get; set; } = 3;

        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>Used when the server sends no caching headers (OSMF policy minimum is 7 days).</summary>
        public TimeSpan FallbackTtl { get; set; } = TimeSpan.FromDays(7);

        /// <summary>Upper bound on freshness so long max-age values do not pin data indefinitely.</summary>
        public TimeSpan MaxTtl { get; set; } = TimeSpan.FromDays(30);

        /// <summary>OpenFreeMap TOS allows at most 4 parallel requests.</summary>
        public int MaxParallelRequests { get; set; } = 4;
    }

    public static class MapUserAgent
    {
        public const string ProjectUrl = "https://github.com/mromanowski832-ctrl/MetinAR-2026";

        public const string Default = "MetinAR/0.1-osm-poc (+" + ProjectUrl + ")";

        public static string For(string appVersion)
        {
            string v = string.IsNullOrWhiteSpace(appVersion) ? "0.1" : appVersion.Trim();
            return "MetinAR/" + v + "-osm-poc (+" + ProjectUrl + ")";
        }
    }

    /// <summary>
    /// GET with on-disk HTTP caching (fresh hit → no request; stale → conditional request with
    /// If-None-Match / If-Modified-Since; 304 → reuse), bounded parallelism, per-attempt timeout,
    /// exponential backoff on network errors / 408 / 429 / 5xx, and cancellation.
    /// Falls back to stale cached bytes when the network is unavailable.
    /// </summary>
    public sealed class CachedHttpFetcher
    {
        private readonly ITileHttpTransport _transport;
        private readonly DiskTileCache _cache;
        private readonly MapHttpOptions _options;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly SemaphoreSlim _parallel;

        public CachedHttpFetcher(
            ITileHttpTransport transport,
            DiskTileCache cache,
            MapHttpOptions options,
            Func<DateTimeOffset> clock = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _cache = cache;
            _options = options ?? new MapHttpOptions();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
            _parallel = new SemaphoreSlim(Math.Max(1, _options.MaxParallelRequests));
        }

        public MapHttpOptions Options => _options;

        /// <summary>Highest number of requests that were in flight at once (for tests/diagnostics).</summary>
        public int PeakInFlight { get; private set; }

        private int _inFlight;

        public async Task<TileFetchResult> GetAsync(string url, string cacheKey, CancellationToken cancellationToken)
        {
            var result = new TileFetchResult();
            TileCacheEntry cached = null;
            if (_cache != null && cacheKey != null && _cache.TryGet(cacheKey, out cached) && cached.FreshUntil > _clock())
            {
                result.Status = TileFetchStatus.Ok;
                result.Data = cached.Data;
                result.FromCache = true;
                return result;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User-Agent"] = _options.UserAgent
            };
            if (cached != null)
            {
                if (!string.IsNullOrEmpty(cached.ETag))
                {
                    headers["If-None-Match"] = cached.ETag;
                }

                if (!string.IsNullOrEmpty(cached.LastModified))
                {
                    headers["If-Modified-Since"] = cached.LastModified;
                }
            }

            TimeSpan backoff = _options.InitialBackoff;
            string lastError = null;
            int attempts = Math.Max(1, _options.MaxAttempts);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = TileFetchStatus.Cancelled;
                    result.Attempts = attempt - 1;
                    return result;
                }

                result.Attempts = attempt;
                HttpTileResponse response;
                try
                {
                    await _parallel.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Status = TileFetchStatus.Cancelled;
                    return result;
                }

                try
                {
                    int now = Interlocked.Increment(ref _inFlight);
                    if (now > PeakInFlight)
                    {
                        PeakInFlight = now;
                    }

                    response = await _transport.GetAsync(url, headers, _options.Timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Status = TileFetchStatus.Cancelled;
                    return result;
                }
                catch (Exception ex)
                {
                    response = new HttpTileResponse { StatusCode = 0, Error = ex.Message };
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                    _parallel.Release();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = TileFetchStatus.Cancelled;
                    return result;
                }

                int code = response.StatusCode;
                if (code == 200)
                {
                    byte[] body = response.Body ?? Array.Empty<byte>();
                    result.NetworkBytes = body.LongLength;
                    CacheDirective directive = HttpCachePolicy.Evaluate(response.Headers, _clock(), _options.FallbackTtl, _options.MaxTtl);
                    if (_cache != null && cacheKey != null && directive.Store)
                    {
                        _cache.Put(cacheKey, new TileCacheEntry
                        {
                            Data = body,
                            FreshUntil = directive.FreshUntil,
                            ETag = directive.ETag,
                            LastModified = directive.LastModified
                        });
                    }

                    result.Status = body.Length == 0 ? TileFetchStatus.Empty : TileFetchStatus.Ok;
                    result.Data = body;
                    return result;
                }

                if (code == 304 && cached != null)
                {
                    CacheDirective directive = HttpCachePolicy.Evaluate(response.Headers, _clock(), _options.FallbackTtl, _options.MaxTtl);
                    _cache?.Refresh(cacheKey, directive.FreshUntil, directive.ETag, directive.LastModified);
                    result.Status = TileFetchStatus.Ok;
                    result.Data = cached.Data;
                    result.FromCache = true;
                    return result;
                }

                if (code == 204 || code == 404)
                {
                    result.Status = TileFetchStatus.Empty;
                    result.Data = Array.Empty<byte>();
                    return result;
                }

                lastError = code == 0
                    ? (response.TimedOut ? "timeout" : "network: " + (response.Error ?? "unknown"))
                    : "HTTP " + code;

                bool retryable = code == 0 || code == 408 || code == 429 || code >= 500;
                if (!retryable || attempt == attempts)
                {
                    break;
                }

                TimeSpan wait = backoff;
                if (code == 429 && response.Headers.TryGetValue("Retry-After", out string ra)
                    && int.TryParse(ra.Trim(), out int raSeconds))
                {
                    wait = TimeSpan.FromSeconds(Math.Max(0, raSeconds));
                }

                if (wait > _options.MaxBackoff)
                {
                    wait = _options.MaxBackoff;
                }

                try
                {
                    await _delay(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Status = TileFetchStatus.Cancelled;
                    return result;
                }

                backoff = TimeSpan.FromTicks(Math.Min(_options.MaxBackoff.Ticks, backoff.Ticks * 2));
            }

            if (cached != null)
            {
                result.Status = TileFetchStatus.Ok;
                result.Data = cached.Data;
                result.FromCache = true;
                result.Stale = true;
                result.Error = lastError;
                return result;
            }

            result.Status = TileFetchStatus.Failed;
            result.Error = lastError;
            return result;
        }
    }
}
