using System.Diagnostics;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// Aggregate, coordinate-free timing / size counters for the OSM POC (P8 evidence).
    /// Written on the main thread only.
    /// </summary>
    public sealed class MapViewStats
    {
        private readonly Stopwatch _sinceOrigin = new Stopwatch();

        public int TilesRequested { get; internal set; }
        public int TilesBuilt { get; internal set; }
        public int TilesFailed { get; internal set; }
        public int TilesFromCache { get; internal set; }
        public long NetworkBytes { get; internal set; }
        public long PayloadBytes { get; internal set; }
        public double FetchMsTotal { get; internal set; }
        public double DecodeMsTotal { get; internal set; }
        public double DecodeMsMax { get; internal set; }
        public double MeshMsTotal { get; internal set; }
        public double MeshMsMax { get; internal set; }
        public double UploadMsTotal { get; internal set; }
        public double UploadMsMax { get; internal set; }
        public int Features { get; internal set; }
        public int Vertices { get; internal set; }
        public int Triangles { get; internal set; }
        public int Renderers { get; internal set; }

        /// <summary>Wall time from origin capture until every tile of the first neighbourhood was built; -1 = not yet.</summary>
        public double InitialAreaMs { get; internal set; } = -1;
        public long InitialAreaNetworkBytes { get; internal set; } = -1;
        public long InitialAreaPayloadBytes { get; internal set; } = -1;

        internal void StartClock() => _sinceOrigin.Restart();

        internal double ElapsedMs => _sinceOrigin.Elapsed.TotalMilliseconds;

        public string Summary()
        {
            return "tiles=" + TilesBuilt + "/" + TilesRequested + " failed=" + TilesFailed + " cached=" + TilesFromCache
                   + " netKB=" + (NetworkBytes / 1024) + " payloadKB=" + (PayloadBytes / 1024)
                   + " decodeMs(max)=" + DecodeMsMax.ToString("F1") + " meshMs(max)=" + MeshMsMax.ToString("F1")
                   + " uploadMs(max)=" + UploadMsMax.ToString("F1") + " verts=" + Vertices + " tris=" + Triangles
                   + " renderers=" + Renderers + " initialAreaMs=" + InitialAreaMs.ToString("F0")
                   + " initialNetKB=" + (InitialAreaNetworkBytes < 0 ? -1 : InitialAreaNetworkBytes / 1024);
        }
    }
}
