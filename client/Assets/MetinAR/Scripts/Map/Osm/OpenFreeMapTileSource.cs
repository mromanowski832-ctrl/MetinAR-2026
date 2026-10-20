using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MetinAR.Map.Osm
{
    /// <summary>
    /// <see cref="IMapTileSource"/> for the OpenFreeMap public instance (or any TileJSON 3.0 vector source).
    /// No account, API key or cookies. The TileJSON URL is configuration, not hard-coded logic, so the
    /// host can be switched (e.g. to self-hosted PMTiles behind HTTP) without a code change.
    /// Privacy: every request reveals the requested tile (≈ area around the player) and the device IP
    /// to the tile host / its CDN. No player id, coordinates or other telemetry is sent.
    /// </summary>
    public sealed class OpenFreeMapTileSource : IMapTileSource
    {
        public const string DefaultTileJsonUrl = "https://tiles.openfreemap.org/planet";

        /// <summary>Required attribution per https://openfreemap.org (used when TileJSON has none).</summary>
        public static readonly string[] DefaultAttribution =
        {
            "OpenFreeMap",
            "© OpenMapTiles",
            "Data from OpenStreetMap"
        };

        public const string OsmCopyrightUrl = "https://www.openstreetmap.org/copyright";

        private readonly CachedHttpFetcher _fetcher;
        private readonly string _tileJsonUrl;
        private string _template;
        private List<string> _attribution = new List<string>(DefaultAttribution);

        public OpenFreeMapTileSource(CachedHttpFetcher fetcher, string tileJsonUrl = DefaultTileJsonUrl, string tileUrlTemplateOverride = null)
        {
            _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _tileJsonUrl = string.IsNullOrWhiteSpace(tileJsonUrl) ? DefaultTileJsonUrl : tileJsonUrl.Trim();
            _template = string.IsNullOrWhiteSpace(tileUrlTemplateOverride) ? null : tileUrlTemplateOverride.Trim();
        }

        public string Id => "openfreemap";

        public int SourceMaxZoom { get; private set; } = 14;

        public IReadOnlyList<string> Attribution => _attribution;

        public string AttributionUrl => OsmCopyrightUrl;

        public string TileUrlTemplate => _template;

        public bool IsInitialized => _template != null;

        public string LastError { get; private set; }

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
        {
            if (_template != null)
            {
                return true;
            }

            TileFetchResult r = await _fetcher.GetAsync(_tileJsonUrl, Id + "/tilejson", cancellationToken).ConfigureAwait(false);
            if (r.Status != TileFetchStatus.Ok || r.Data == null)
            {
                LastError = "TileJSON " + r.Status + " " + r.Error;
                return false;
            }

            try
            {
                ApplyTileJson(Encoding.UTF8.GetString(r.Data));
                return _template != null;
            }
            catch (FormatException ex)
            {
                LastError = "TileJSON parse: " + ex.Message;
                return false;
            }
        }

        /// <summary>Reads tiles[0], maxzoom and attribution from a TileJSON document.</summary>
        public void ApplyTileJson(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> doc))
            {
                throw new FormatException("TileJSON root must be an object.");
            }

            if (doc.TryGetValue("tiles", out object tilesObj) && tilesObj is List<object> tiles && tiles.Count > 0 && tiles[0] is string t0
                && t0.Contains("{z}") && t0.Contains("{x}") && t0.Contains("{y}"))
            {
                if (!t0.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException("Tile URL must use HTTPS.");
                }

                _template = t0;
            }
            else
            {
                throw new FormatException("TileJSON has no usable tiles[] template.");
            }

            if (doc.TryGetValue("maxzoom", out object mz) && mz is double d && d >= 0 && d <= TileId.MaxZoom)
            {
                SourceMaxZoom = (int)d;
            }

            if (doc.TryGetValue("attribution", out object att) && att is string html)
            {
                List<string> parsed = AttributionFromHtml(html);
                if (parsed.Count > 0)
                {
                    _attribution = parsed;
                }
            }
        }

        public string UrlFor(TileId tile)
        {
            if (_template == null)
            {
                throw new InvalidOperationException("Source not initialized.");
            }

            return _template.Replace("{z}", tile.Z.ToString()).Replace("{x}", tile.X.ToString()).Replace("{y}", tile.Y.ToString());
        }

        public async Task<TileFetchResult> FetchAsync(TileId tile, CancellationToken cancellationToken)
        {
            if (tile.Z > SourceMaxZoom)
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Request the ancestor at SourceMaxZoom instead.");
            }

            if (_template == null && !await InitializeAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TileFetchResult { Status = TileFetchStatus.Failed, Error = LastError };
            }

            return await _fetcher.GetAsync(UrlFor(tile), Id + "/" + StableHash(_template) + "/" + tile, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Turns the TileJSON HTML attribution into plain text segments (one per link/text run).</summary>
        public static List<string> AttributionFromHtml(string html)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(html))
            {
                return parts;
            }

            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<')
                {
                    inTag = true;
                    Flush(sb, parts);
                    continue;
                }

                if (c == '>')
                {
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    sb.Append(c);
                }
            }

            Flush(sb, parts);
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i] = parts[i].Replace("&copy;", "©").Replace("&amp;", "&").Replace("&nbsp;", " ").Trim();
            }

            parts.RemoveAll(string.IsNullOrWhiteSpace);
            return parts;
        }

        private static void Flush(StringBuilder sb, List<string> parts)
        {
            if (sb.Length > 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
        }

        /// <summary>Deterministic (FNV-1a) hash; string.GetHashCode is randomized per process on .NET.</summary>
        public static string StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s)
                {
                    h = (h ^ c) * 16777619;
                }

                return h.ToString("x8");
            }
        }
    }
}
