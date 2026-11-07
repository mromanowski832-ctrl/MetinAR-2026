using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetinAR.Map
{
    public enum TileFetchStatus
    {
        Ok = 0,
        /// <summary>Provider has no tile here (404/204): treated as an empty tile, not an error.</summary>
        Empty = 1,
        Failed = 2,
        Cancelled = 3
    }

    public sealed class TileFetchResult
    {
        public TileFetchStatus Status { get; set; }

        public byte[] Data { get; set; }

        public bool FromCache { get; set; }

        /// <summary>True when stale cached bytes were served because the network failed.</summary>
        public bool Stale { get; set; }

        /// <summary>Body bytes received from the network for this request (0 on cache hit / 304).</summary>
        public long NetworkBytes { get; set; }

        public int Attempts { get; set; }

        public string Error { get; set; }
    }

    /// <summary>Delivers raw tile bytes for an address. Implementations own caching and transport.</summary>
    public interface IMapTileSource
    {
        /// <summary>Short id, also used as disk-cache namespace.</summary>
        string Id { get; }

        /// <summary>Highest zoom the source has data for (higher display zooms over-zoom from it).</summary>
        int SourceMaxZoom { get; }

        /// <summary>Attribution lines that must stay visible while this source's data is shown.</summary>
        IReadOnlyList<string> Attribution { get; }

        /// <summary>Where users can read the data licence (e.g. openstreetmap.org/copyright).</summary>
        string AttributionUrl { get; }

        Task<bool> InitializeAsync(CancellationToken cancellationToken);

        Task<TileFetchResult> FetchAsync(TileId tile, CancellationToken cancellationToken);
    }

    /// <summary>Turns raw tile bytes into provider-independent <see cref="MapFeature"/>s.</summary>
    public interface IMapFeatureDecoder
    {
        List<MapFeature> Decode(TileId tile, byte[] data);
    }
}
