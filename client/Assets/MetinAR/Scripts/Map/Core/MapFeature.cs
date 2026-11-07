using System.Collections.Generic;

namespace MetinAR.Map
{
    /// <summary>Provider-independent map feature categories used by rendering and spawn safety.</summary>
    public enum MapFeatureKind
    {
        Landcover = 0,
        Landuse = 1,
        Park = 2,
        Water = 3,
        Waterway = 4,
        Road = 5,
        Rail = 6,
        Building = 7
    }

    /// <summary>Road hierarchy used for styling and safety buffers.</summary>
    public enum RoadClass
    {
        None = 0,
        Motorway = 1,
        Trunk = 2,
        Primary = 3,
        Secondary = 4,
        Tertiary = 5,
        Minor = 6,
        Service = 7,
        Track = 8,
        Path = 9,
        Pedestrian = 10
    }

    /// <summary>A point in normalized source-tile coordinates: u east, v south, both 0..1 inside the tile.</summary>
    public readonly struct MapPoint
    {
        public MapPoint(double u, double v)
        {
            U = u;
            V = v;
        }

        public double U { get; }

        public double V { get; }
    }

    public sealed class MapPolygon
    {
        public MapPolygon(List<MapPoint> exterior)
        {
            Exterior = exterior;
        }

        public List<MapPoint> Exterior { get; }

        public List<List<MapPoint>> Holes { get; } = new List<List<MapPoint>>();
    }

    /// <summary>
    /// Decoded, classified feature of one source tile. Contains no vendor/schema types; adapters
    /// (e.g. OpenMapTiles) translate their own layer/class names into this model.
    /// </summary>
    public sealed class MapFeature
    {
        public MapFeature(TileId sourceTile, MapFeatureKind kind)
        {
            SourceTile = sourceTile;
            Kind = kind;
        }

        public TileId SourceTile { get; }

        public MapFeatureKind Kind { get; }

        public RoadClass RoadClass { get; set; }

        /// <summary>Normalized land/water class (e.g. "residential", "park", "river"); may be empty.</summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>True when the source marks the feature as private / no public access.</summary>
        public bool IsPrivate { get; set; }

        /// <summary>True for bridges (rendered slightly raised); tunnels are dropped by adapters.</summary>
        public bool IsBridge { get; set; }

        public float HeightMeters { get; set; }

        public float MinHeightMeters { get; set; }

        public List<List<MapPoint>> Lines { get; } = new List<List<MapPoint>>();

        public List<MapPolygon> Polygons { get; } = new List<MapPolygon>();
    }
}
