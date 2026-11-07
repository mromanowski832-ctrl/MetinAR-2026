using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MetinAR.Location;
using MetinAR.Map.Osm;
using MetinAR.WorldMap;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// METINAR-008B real-world map renderer (proof of concept).
    /// Reads the shared <see cref="WorldMapOrigin"/> (never captures it) and the follow target's local position,
    /// keeps the 3×3 display-zoom neighbourhood around it loaded from <see cref="IMapTileSource"/>,
    /// decodes / projects / meshes off the main thread and uploads one mesh per <see cref="MapRenderLayer"/>
    /// per source tile. It also feeds <see cref="FeatureSpawnSafetyService"/> with the same features.
    /// Gameplay code only sees <see cref="ISpawnSafetyService"/> + <see cref="GeoCoordinate"/>; no provider
    /// types leak out. Privacy: never logs tile ids, URLs or coordinates (a tile id reveals the area).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class OsmMapView : MonoBehaviour
    {
        private enum SourceState { NotStarted, Initializing, Ready, Failed }

        private enum TileState { Loading, Decoded, Built, Failed }

        private sealed class TileEntry
        {
            public TileState State;
            public List<MapFeature> Features;
            public GameObject Root;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public float RetryAt;
        }

        private sealed class PreparedLayer
        {
            public MapRenderLayer Layer;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public int[] Indices;
        }

        private sealed class PreparedTile
        {
            public List<ProjectedFeature> Projected;
            public readonly List<PreparedLayer> Layers = new List<PreparedLayer>();
            public double MeshMs;
            public int Vertices;
            public int Triangles;
        }

        [SerializeField] private WorldMapOrigin _origin;
        [SerializeField] private Transform _followTarget;
        [SerializeField] private MapStyleProfile _style;
        [SerializeField] private string _tileJsonUrl = OpenFreeMapTileSource.DefaultTileJsonUrl;
        [SerializeField, Range(14, 18)] private int _displayZoom = 16;
        [SerializeField, Range(0, 2)] private int _neighborhoodRadius = 1;
        [SerializeField, Range(8, 512)] private int _diskCacheMegabytes = 64;
        [SerializeField, Range(3f, 60f)] private float _requestTimeoutSeconds = 15f;
        [SerializeField, Range(1, 5)] private int _maxAttempts = 3;
        [SerializeField, Range(5f, 120f)] private float _failedTileRetrySeconds = 20f;
        [SerializeField] private bool _logTileStats = true;

        private static readonly Regex UrlPattern = new Regex(@"\w+://\S+", RegexOptions.Compiled);

        private readonly Dictionary<TileId, TileEntry> _tiles = new Dictionary<TileId, TileEntry>();
        private readonly FeatureSpawnSafetyService _safety = new FeatureSpawnSafetyService();
        private readonly OpenMapTilesDecoder _decoder = new OpenMapTilesDecoder();
        private readonly MapViewStats _stats = new MapViewStats();
        private CancellationTokenSource _cts;
        private IMapTileSource _source;
        private SourceState _sourceState;
        private LocalTangentFrame _frame;
        private MapMeshStyle _meshStyle;
        private bool _hasCenter;
        private TileId _center;
        private int _generation;
        private GeoBounds _window;
        private double _winMinE, _winMinN, _winMaxE, _winMaxN;
        private HashSet<TileId> _initialPending;
        private List<TileId> _needed = new List<TileId>();

        public event Action<OsmMapView> InitialAreaReady;

        public WorldMapOrigin Origin => _origin;
        public Transform FollowTarget => _followTarget;
        public int DisplayZoom => _displayZoom;
        public MapViewStats Stats => _stats;

        /// <summary>Spawn-safety queries over the currently loaded map data (Unknown outside it).</summary>
        public ISpawnSafetyService Safety => _safety;

        public bool IsSourceReady => _sourceState == SourceState.Ready;
        public bool IsSourceFailed => _sourceState == SourceState.Failed;
        public bool IsInitialAreaReady { get; private set; }
        public string LastError { get; private set; }

        public IReadOnlyList<string> Attribution =>
            _source != null ? _source.Attribution : OpenFreeMapTileSource.DefaultAttribution;

        public string AttributionUrl => _source != null ? _source.AttributionUrl : OpenFreeMapTileSource.OsmCopyrightUrl;

        public int BuiltTileCount
        {
            get
            {
                int n = 0;
                foreach (TileEntry e in _tiles.Values)
                {
                    if (e.State == TileState.Built)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public void Configure(WorldMapOrigin origin, Transform followTarget, MapStyleProfile style)
        {
            _origin = origin;
            _followTarget = followTarget;
            _style = style;
        }

        /// <summary>Features currently backing the rendered meshes (for diagnostics / smoke tests).</summary>
        public IReadOnlyList<ProjectedFeature> BuiltFeatures => _safety.Features;

        /// <summary>Triangle count of a render layer over all built tiles.</summary>
        public int TrianglesIn(MapRenderLayer layer)
        {
            int n = 0;
            string name = layer.ToString();
            foreach (TileEntry e in _tiles.Values)
            {
                foreach (Mesh m in e.Meshes)
                {
                    if (m != null && m.name == name)
                    {
                        n += (int)(m.GetIndexCount(0) / 3);
                    }
                }
            }

            return n;
        }

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            _meshStyle = _style != null ? _style.CreateMeshStyle() : new MapMeshStyle();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            foreach (TileEntry e in _tiles.Values)
            {
                DestroyTileObjects(e);
            }

            _tiles.Clear();
            _cts?.Dispose();
            _cts = null;
        }

        private void Update()
        {
            if (_origin == null || !_origin.HasOrigin)
            {
                return;
            }

            if (_frame == null)
            {
                _frame = new LocalTangentFrame(_origin.Origin);
                _safety.SetFrame(_frame);
                _stats.StartClock();
            }

            if (_sourceState == SourceState.NotStarted)
            {
                Observe(InitializeSourceAsync());
                return;
            }

            if (_sourceState != SourceState.Ready)
            {
                return;
            }

            Vector3 p = _followTarget != null ? _followTarget.position : Vector3.zero;
            TileId center = WebMercator.TileFor(_origin.LocalToWorld(p.x, p.z), _displayZoom);
            if (!_hasCenter || center != _center)
            {
                SetWindow(center);
            }

            RetryFailedTiles();
        }

        private async Task InitializeSourceAsync()
        {
            _sourceState = SourceState.Initializing;
            string cacheRoot = Path.Combine(Application.temporaryCachePath, "MetinARMapTiles");
            var cache = new DiskTileCache(cacheRoot, (long)_diskCacheMegabytes * 1024 * 1024);
            var options = new MapHttpOptions
            {
                UserAgent = MapUserAgent.For(Application.version),
                Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds),
                MaxAttempts = _maxAttempts
            };
            var fetcher = new CachedHttpFetcher(new UnityWebRequestTransport(), cache, options);
            var source = new OpenFreeMapTileSource(fetcher, _tileJsonUrl);
            _source = source;
            bool ok = await source.InitializeAsync(_cts.Token);
            if (this == null)
            {
                return;
            }

            if (!ok)
            {
                _sourceState = SourceState.Failed;
                LastError = Sanitize(source.LastError);
                Debug.LogWarning("[OsmMap] Tile source unavailable: " + LastError);
                return;
            }

            _sourceState = SourceState.Ready;
            Debug.Log("[OsmMap] Tile source ready (maxzoom " + source.SourceMaxZoom + ").");
        }

        private void SetWindow(TileId center)
        {
            bool first = !_hasCenter;
            _hasCenter = true;
            _center = center;
            _generation++;

            List<TileId> display = WebMercator.Neighborhood(center, _neighborhoodRadius);
            _window = WebMercator.Bounds(display);
            _frame.ToLocalMeters(_window.South, _window.West, out _winMinE, out _winMinN);
            _frame.ToLocalMeters(_window.North, _window.East, out _winMaxE, out _winMaxN);
            int sourceZoom = Math.Min(_displayZoom, _source.SourceMaxZoom);
            _needed = WebMercator.SourceTilesFor(display, sourceZoom);

            var keep = new HashSet<TileId>(_needed);
            var drop = new List<TileId>();
            foreach (TileId t in _tiles.Keys)
            {
                if (!keep.Contains(t))
                {
                    drop.Add(t);
                }
            }

            foreach (TileId t in drop)
            {
                DestroyTileObjects(_tiles[t]);
                _tiles.Remove(t);
                _safety.RemoveTile(t);
            }

            if (first)
            {
                _initialPending = new HashSet<TileId>(_needed);
            }

            foreach (TileId t in _needed)
            {
                if (!_tiles.TryGetValue(t, out TileEntry entry))
                {
                    entry = new TileEntry { State = TileState.Loading };
                    _tiles[t] = entry;
                    _stats.TilesRequested++;
                    Observe(LoadTileAsync(t, entry));
                }
                else if (entry.Features != null)
                {
                    Observe(BuildTileAsync(t, entry));
                }
            }
        }

        private void RetryFailedTiles()
        {
            float now = Time.unscaledTime;
            foreach (TileId t in _needed)
            {
                if (_tiles.TryGetValue(t, out TileEntry e) && e.State == TileState.Failed && now >= e.RetryAt)
                {
                    e.State = TileState.Loading;
                    Observe(LoadTileAsync(t, e));
                }
            }
        }

        private async Task LoadTileAsync(TileId tile, TileEntry entry)
        {
            CancellationToken ct = _cts.Token;
            var sw = Stopwatch.StartNew();
            TileFetchResult result = await _source.FetchAsync(tile, ct);
            if (this == null || ct.IsCancellationRequested || !IsCurrent(tile, entry))
            {
                return;
            }

            _stats.FetchMsTotal += sw.Elapsed.TotalMilliseconds;
            _stats.NetworkBytes += result.NetworkBytes;
            if (result.FromCache)
            {
                _stats.TilesFromCache++;
            }

            if (result.Status == TileFetchStatus.Failed || result.Status == TileFetchStatus.Cancelled)
            {
                entry.State = TileState.Failed;
                entry.RetryAt = Time.unscaledTime + _failedTileRetrySeconds;
                _stats.TilesFailed++;
                LastError = Sanitize(result.Error);
                Debug.LogWarning("[OsmMap] Tile fetch failed after " + result.Attempts + " attempt(s): " + LastError);
                MarkInitialDone(tile);
                return;
            }

            byte[] data = result.Data;
            _stats.PayloadBytes += data != null ? data.Length : 0;
            double decodeMs = 0;
            List<MapFeature> features;
            try
            {
                features = await Task.Run(() =>
                {
                    var dsw = Stopwatch.StartNew();
                    List<MapFeature> f = _decoder.Decode(tile, data);
                    decodeMs = dsw.Elapsed.TotalMilliseconds;
                    return f;
                }, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                if (this == null)
                {
                    return;
                }

                entry.State = TileState.Failed;
                entry.RetryAt = float.PositiveInfinity; // corrupt data will not fix itself
                _stats.TilesFailed++;
                LastError = "decode: " + e.GetType().Name;
                Debug.LogWarning("[OsmMap] Tile decode failed: " + Sanitize(e.Message));
                MarkInitialDone(tile);
                return;
            }

            if (this == null || ct.IsCancellationRequested || !IsCurrent(tile, entry))
            {
                return;
            }

            _stats.DecodeMsTotal += decodeMs;
            _stats.DecodeMsMax = Math.Max(_stats.DecodeMsMax, decodeMs);
            entry.Features = features;
            entry.State = TileState.Decoded;
            await BuildTileAsync(tile, entry);
        }

        private async Task BuildTileAsync(TileId tile, TileEntry entry)
        {
            CancellationToken ct = _cts.Token;
            int generation = _generation;
            List<MapFeature> features = entry.Features;
            LocalTangentFrame frame = _frame;
            MapMeshStyle style = _meshStyle;
            double minE = _winMinE, minN = _winMinN, maxE = _winMaxE, maxN = _winMaxN;
            GeoBounds coverage = Intersect(_window, WebMercator.Bounds(tile));

            PreparedTile prepared;
            try
            {
                prepared = await Task.Run(() => Prepare(features, frame, style, minE, minN, maxE, maxN), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (this == null || ct.IsCancellationRequested || !IsCurrent(tile, entry) || generation != _generation)
            {
                return; // a newer window superseded this build
            }

            var sw = Stopwatch.StartNew();
            DestroyTileObjects(entry);
            entry.Root = Upload(prepared, entry);
            double uploadMs = sw.Elapsed.TotalMilliseconds;
            _safety.SetTile(tile, coverage, prepared.Projected);
            bool firstBuild = entry.State != TileState.Built;
            entry.State = TileState.Built;

            _stats.MeshMsTotal += prepared.MeshMs;
            _stats.MeshMsMax = Math.Max(_stats.MeshMsMax, prepared.MeshMs);
            _stats.UploadMsTotal += uploadMs;
            _stats.UploadMsMax = Math.Max(_stats.UploadMsMax, uploadMs);
            if (firstBuild)
            {
                _stats.TilesBuilt++;
            }

            RecountTotals();
            if (_logTileStats)
            {
                Debug.Log("[OsmMap] tile built: features=" + prepared.Projected.Count + " verts=" + prepared.Vertices
                          + " tris=" + prepared.Triangles + " meshMs=" + prepared.MeshMs.ToString("F1")
                          + " uploadMs=" + uploadMs.ToString("F1") + " | " + _stats.Summary());
            }

            MarkInitialDone(tile);
        }

        private static PreparedTile Prepare(List<MapFeature> features, LocalTangentFrame frame, MapMeshStyle style,
            double minE, double minN, double maxE, double maxN)
        {
            var sw = Stopwatch.StartNew();
            var tile = new PreparedTile
            {
                Projected = FeatureProjector.ProjectAndCull(features, frame, minE, minN, maxE, maxN)
            };
            Dictionary<MapRenderLayer, MapMeshData> layers = MapMeshBuilder.Build(tile.Projected, style);
            foreach (KeyValuePair<MapRenderLayer, MapMeshData> kv in layers)
            {
                MapMeshData d = kv.Value;
                if (d.VertexCount == 0 || d.Indices.Count == 0)
                {
                    continue;
                }

                var pl = new PreparedLayer
                {
                    Layer = kv.Key,
                    Positions = new Vector3[d.VertexCount],
                    Normals = new Vector3[d.VertexCount],
                    Indices = d.Indices.ToArray()
                };
                for (int i = 0; i < d.VertexCount; i++)
                {
                    pl.Positions[i] = new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                    pl.Normals[i] = new Vector3(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                }

                tile.Vertices += d.VertexCount;
                tile.Triangles += d.TriangleCount;
                tile.Layers.Add(pl);
            }

            tile.MeshMs = sw.Elapsed.TotalMilliseconds;
            return tile;
        }

        private GameObject Upload(PreparedTile prepared, TileEntry entry)
        {
            var root = new GameObject("OsmTile");
            root.transform.SetParent(transform, false);

            foreach (PreparedLayer pl in prepared.Layers)
            {
                var mesh = new Mesh
                {
                    name = pl.Layer.ToString(),
                    indexFormat = pl.Positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(pl.Positions);
                mesh.SetNormals(pl.Normals);
                mesh.SetTriangles(pl.Indices, 0, true);
                mesh.UploadMeshData(false);

                var go = new GameObject(pl.Layer.ToString());
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _style != null ? _style.MaterialFor(pl.Layer) : null;
                bool building = pl.Layer == MapRenderLayer.Building;
                mr.shadowCastingMode = building && _style != null && _style.BuildingsCastShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                mr.receiveShadows = building;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                entry.Meshes.Add(mesh);
            }

            return root;
        }

        private void DestroyTileObjects(TileEntry e)
        {
            foreach (Mesh m in e.Meshes)
            {
                if (m != null)
                {
                    Destroy(m);
                }
            }

            e.Meshes.Clear();
            if (e.Root != null)
            {
                Destroy(e.Root);
                e.Root = null;
            }
        }

        private void RecountTotals()
        {
            int verts = 0, tris = 0, renderers = 0, features = 0;
            foreach (TileEntry e in _tiles.Values)
            {
                foreach (Mesh m in e.Meshes)
                {
                    if (m == null)
                    {
                        continue;
                    }

                    verts += m.vertexCount;
                    tris += (int)(m.GetIndexCount(0) / 3);
                    renderers++;
                }
            }

            features = _safety.Features.Count;
            _stats.Vertices = verts;
            _stats.Triangles = tris;
            _stats.Renderers = renderers;
            _stats.Features = features;
        }

        private void MarkInitialDone(TileId tile)
        {
            if (_initialPending == null || !_initialPending.Remove(tile) || _initialPending.Count > 0)
            {
                return;
            }

            _initialPending = null;
            IsInitialAreaReady = true;
            _stats.InitialAreaMs = _stats.ElapsedMs;
            _stats.InitialAreaNetworkBytes = _stats.NetworkBytes;
            _stats.InitialAreaPayloadBytes = _stats.PayloadBytes;
            Debug.Log("[OsmMap] initial area ready: " + _stats.Summary());
            InitialAreaReady?.Invoke(this);
        }

        private bool IsCurrent(TileId tile, TileEntry entry)
        {
            return _tiles.TryGetValue(tile, out TileEntry current) && ReferenceEquals(current, entry);
        }

        private static GeoBounds Intersect(GeoBounds a, GeoBounds b)
        {
            return new GeoBounds(
                Math.Max(a.West, b.West),
                Math.Max(a.South, b.South),
                Math.Min(a.East, b.East),
                Math.Min(a.North, b.North));
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "unknown error";
            }

            return UrlPattern.Replace(message, "<url>");
        }

        private static async void Observe(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OsmMap] " + e.GetType().Name + ": " + Sanitize(e.Message));
            }
        }
    }
}
