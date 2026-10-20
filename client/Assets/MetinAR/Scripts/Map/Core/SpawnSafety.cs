using System;
using System.Collections.Generic;
using MetinAR.Location;

namespace MetinAR.Map
{
    public enum SafetyVerdict
    {
        Allowed = 0,
        Blocked = 1,
        /// <summary>No usable map data for the point. Callers must treat this as deny (default-deny).</summary>
        Unknown = 2
    }

    /// <summary>Why a point was blocked (or Unknown). Provider-independent.</summary>
    public enum SafetyReason
    {
        None = 0,
        Road = 1,
        Rail = 2,
        Water = 3,
        Building = 4,
        Private = 5,
        NoData = 6,
        UnclassifiedSurface = 7
    }

    public readonly struct SafetyResult
    {
        public SafetyResult(SafetyVerdict verdict, SafetyReason reason, string detail)
        {
            Verdict = verdict;
            Reason = reason;
            Detail = detail ?? string.Empty;
        }

        public SafetyVerdict Verdict { get; }

        public SafetyReason Reason { get; }

        /// <summary>e.g. road class or land-use class; never contains coordinates.</summary>
        public string Detail { get; }

        /// <summary>Default-deny: only an explicit Allowed permits a spawn.</summary>
        public bool IsSpawnAllowed => Verdict == SafetyVerdict.Allowed;

        public override string ToString()
        {
            return Verdict + (Reason == SafetyReason.None ? string.Empty : "(" + Reason + (Detail.Length > 0 ? ":" + Detail : string.Empty) + ")");
        }
    }

    /// <summary>Provider-independent spawn-safety query used by gameplay / spawn placement.</summary>
    public interface ISpawnSafetyService
    {
        /// <param name="clearanceMeters">Extra margin, e.g. current GPS horizontal accuracy.</param>
        SafetyResult Evaluate(GeoCoordinate point, double clearanceMeters);

        /// <summary>Searches rings around <paramref name="near"/> for an Allowed point.</summary>
        bool TryFindSafePoint(GeoCoordinate near, double searchRadiusMeters, double clearanceMeters, out GeoCoordinate safePoint);
    }

    /// <summary>
    /// PROTOTYPE buffer values (meters) for <see cref="FeatureSpawnSafetyService"/>. Not owner-approved policy:
    /// METINAR-008 §9 Q7 asks the owner to confirm categories, buffers and default-deny.
    /// A buffer is measured from the road / rail / waterway centerline or from a polygon edge; points inside
    /// water, building or restricted polygons are always blocked.
    /// </summary>
    public sealed class SpawnSafetyConfig
    {
        public double MotorwayBuffer { get; set; } = 25;
        public double TrunkBuffer { get; set; } = 20;
        public double PrimaryBuffer { get; set; } = 15;
        public double SecondaryBuffer { get; set; } = 12;
        public double TertiaryBuffer { get; set; } = 10;
        public double MinorBuffer { get; set; } = 6;
        public double ServiceBuffer { get; set; } = 4;
        public double TrackBuffer { get; set; } = 3;

        /// <summary>Footpaths / pedestrian streets are walkable: 0 = not blocking.</summary>
        public double PathBuffer { get; set; } = 0;

        public double PedestrianBuffer { get; set; } = 0;

        /// <summary>Private / no-access roads (any class) use at least this buffer.</summary>
        public double PrivateRoadBuffer { get; set; } = 6;

        public double RailBuffer { get; set; } = 15;
        public double WaterBuffer { get; set; } = 5;
        public double WaterwayBuffer { get; set; } = 6;
        public double BuildingBuffer { get; set; } = 2;
        public double RestrictedAreaBuffer { get; set; } = 0;

        /// <summary>
        /// When true (default) a point inside coverage but outside any known landuse / landcover / park polygon is
        /// Unknown (deny), because OSM-derived tiles cannot prove such places are public.
        /// </summary>
        public bool RequireKnownSurface { get; set; } = true;

        public double RoadBuffer(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: return MotorwayBuffer;
                case RoadClass.Trunk: return TrunkBuffer;
                case RoadClass.Primary: return PrimaryBuffer;
                case RoadClass.Secondary: return SecondaryBuffer;
                case RoadClass.Tertiary: return TertiaryBuffer;
                case RoadClass.Minor: return MinorBuffer;
                case RoadClass.Service: return ServiceBuffer;
                case RoadClass.Track: return TrackBuffer;
                case RoadClass.Path: return PathBuffer;
                case RoadClass.Pedestrian: return PedestrianBuffer;
                default: return MinorBuffer;
            }
        }
    }

    /// <summary>
    /// Spawn-safety stub over projected map features (local meters of one <see cref="LocalTangentFrame"/>).
    /// Coverage = union of registered tile bounds; outside coverage → Unknown (deny).
    /// Priority: Water → Building → Private → Rail → Road → surface check.
    /// </summary>
    public sealed class FeatureSpawnSafetyService : ISpawnSafetyService
    {
        private readonly object _lock = new object();
        private readonly Dictionary<TileId, TileData> _tiles = new Dictionary<TileId, TileData>();
        private List<ProjectedFeature> _all = new List<ProjectedFeature>();
        private LocalTangentFrame _frame;

        public FeatureSpawnSafetyService(SpawnSafetyConfig config = null)
        {
            Config = config ?? new SpawnSafetyConfig();
        }

        public SpawnSafetyConfig Config { get; }

        public int TileCount
        {
            get
            {
                lock (_lock)
                {
                    return _tiles.Count;
                }
            }
        }

        /// <summary>Immutable snapshot of every registered feature (replaced, never mutated, on change).</summary>
        public IReadOnlyList<ProjectedFeature> Features
        {
            get
            {
                lock (_lock)
                {
                    return _all;
                }
            }
        }

        /// <summary>Sets the frame used to convert queries. Clears data registered in another frame.</summary>
        public void SetFrame(LocalTangentFrame frame)
        {
            lock (_lock)
            {
                if (_frame != frame)
                {
                    _tiles.Clear();
                    Rebuild();
                }

                _frame = frame;
            }
        }

        /// <summary>Registers projected features for a tile; <paramref name="coverage"/> is where data is authoritative.</summary>
        public void SetTile(TileId tile, GeoBounds coverage, IReadOnlyList<ProjectedFeature> features)
        {
            lock (_lock)
            {
                _tiles[tile] = new TileData { Coverage = coverage, Features = new List<ProjectedFeature>(features) };
                Rebuild();
            }
        }

        public void RemoveTile(TileId tile)
        {
            lock (_lock)
            {
                if (_tiles.Remove(tile))
                {
                    Rebuild();
                }
            }
        }

        private void Rebuild()
        {
            var all = new List<ProjectedFeature>();
            foreach (TileData t in _tiles.Values)
            {
                all.AddRange(t.Features);
            }

            _all = all;
        }

        public SafetyResult Evaluate(GeoCoordinate point, double clearanceMeters)
        {
            double clearance = double.IsNaN(clearanceMeters) || clearanceMeters < 0 ? 0 : clearanceMeters;
            lock (_lock)
            {
                if (_frame == null)
                {
                    return new SafetyResult(SafetyVerdict.Unknown, SafetyReason.NoData, "no-frame");
                }

                bool covered = false;
                foreach (TileData t in _tiles.Values)
                {
                    if (t.Coverage.Contains(point.Latitude, point.Longitude))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    return new SafetyResult(SafetyVerdict.Unknown, SafetyReason.NoData, "outside-coverage");
                }

                _frame.ToLocalMeters(point, out double e, out double n);
                return EvaluateLocal(_all, e, n, clearance, Config);
            }
        }

        public bool TryFindSafePoint(GeoCoordinate near, double searchRadiusMeters, double clearanceMeters, out GeoCoordinate safePoint)
        {
            safePoint = near;
            if (Evaluate(near, clearanceMeters).IsSpawnAllowed)
            {
                return true;
            }

            double step = 4.0;
            for (double r = step; r <= searchRadiusMeters; r += step)
            {
                int samples = Math.Max(8, (int)(2 * Math.PI * r / step));
                for (int i = 0; i < samples; i++)
                {
                    double a = 2 * Math.PI * i / samples;
                    GeoCoordinate c = GeoMath.OffsetMeters(near, r * Math.Cos(a), r * Math.Sin(a));
                    if (Evaluate(c, clearanceMeters).IsSpawnAllowed)
                    {
                        safePoint = c;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Pure classification of a local-meter point against features (exposed for tests).</summary>
        public static SafetyResult EvaluateLocal(IReadOnlyList<ProjectedFeature> features, double e, double n, double clearance, SpawnSafetyConfig cfg)
        {
            SafetyResult? road = null, rail = null, priv = null, building = null;
            bool knownSurface = false;
            foreach (ProjectedFeature f in features)
            {
                MapFeature src = f.Source;
                switch (f.Kind)
                {
                    case MapFeatureKind.Water:
                        if (NearOrInsidePolygons(f, e, n, cfg.WaterBuffer + clearance))
                        {
                            return new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Water, src.Class);
                        }

                        break;
                    case MapFeatureKind.Waterway:
                        if (NearLines(f, e, n, cfg.WaterwayBuffer + clearance))
                        {
                            return new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Water, src.Class);
                        }

                        break;
                    case MapFeatureKind.Building:
                        if (building == null && NearOrInsidePolygons(f, e, n, cfg.BuildingBuffer + clearance))
                        {
                            building = new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Building, "building");
                        }

                        break;
                    case MapFeatureKind.Rail:
                        if (rail == null && NearLines(f, e, n, cfg.RailBuffer + clearance))
                        {
                            rail = new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Rail, src.Class);
                        }

                        break;
                    case MapFeatureKind.Road:
                        double buffer = cfg.RoadBuffer(src.RoadClass);
                        if (src.IsPrivate)
                        {
                            buffer = Math.Max(buffer, cfg.PrivateRoadBuffer);
                            if (priv == null && buffer > 0 && NearLines(f, e, n, buffer + clearance))
                            {
                                priv = new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Private, "road:" + src.Class);
                            }
                        }
                        else if (road == null && buffer > 0 && NearLines(f, e, n, buffer + clearance))
                        {
                            road = new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Road, src.Class);
                        }

                        break;
                    case MapFeatureKind.Landuse:
                        if (src.IsPrivate)
                        {
                            if (priv == null && NearOrInsidePolygons(f, e, n, cfg.RestrictedAreaBuffer + clearance))
                            {
                                priv = new SafetyResult(SafetyVerdict.Blocked, SafetyReason.Private, src.Class);
                            }
                        }
                        else if (!knownSurface && InsidePolygons(f, e, n))
                        {
                            knownSurface = true;
                        }

                        break;
                    case MapFeatureKind.Landcover:
                    case MapFeatureKind.Park:
                        if (!knownSurface && InsidePolygons(f, e, n))
                        {
                            knownSurface = true;
                        }

                        break;
                }
            }

            if (building.HasValue)
            {
                return building.Value;
            }

            if (priv.HasValue)
            {
                return priv.Value;
            }

            if (rail.HasValue)
            {
                return rail.Value;
            }

            if (road.HasValue)
            {
                return road.Value;
            }

            if (cfg.RequireKnownSurface && !knownSurface)
            {
                return new SafetyResult(SafetyVerdict.Unknown, SafetyReason.UnclassifiedSurface, string.Empty);
            }

            return new SafetyResult(SafetyVerdict.Allowed, SafetyReason.None, string.Empty);
        }

        private static bool NearLines(ProjectedFeature f, double e, double n, double radius)
        {
            if (radius <= 0 || !f.BoundsIntersect(e - radius, n - radius, e + radius, n + radius))
            {
                return false;
            }

            double r2 = radius * radius;
            foreach (double[] line in f.Lines)
            {
                for (int i = 0; i + 3 < line.Length; i += 2)
                {
                    if (Geometry2D.SegmentDistanceSquared(e, n, line[i], line[i + 1], line[i + 2], line[i + 3]) <= r2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool InsidePolygons(ProjectedFeature f, double e, double n)
        {
            if (!f.BoundsIntersect(e, n, e, n))
            {
                return false;
            }

            foreach (ProjectedPolygon p in f.Polygons)
            {
                if (Geometry2D.PointInPolygon(p, e, n))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NearOrInsidePolygons(ProjectedFeature f, double e, double n, double radius)
        {
            double r = Math.Max(0, radius);
            if (!f.BoundsIntersect(e - r, n - r, e + r, n + r))
            {
                return false;
            }

            foreach (ProjectedPolygon p in f.Polygons)
            {
                if (Geometry2D.PointInPolygon(p, e, n))
                {
                    return true;
                }

                if (r > 0 && (Geometry2D.RingDistanceSquared(p.Exterior, e, n) <= r * r))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class TileData
        {
            public GeoBounds Coverage;
            public List<ProjectedFeature> Features;
        }
    }
}
