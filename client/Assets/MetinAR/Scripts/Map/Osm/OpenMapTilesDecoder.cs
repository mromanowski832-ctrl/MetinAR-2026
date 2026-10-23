using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MetinAR.Map.Osm
{
    /// <summary>
    /// Adapter from the OpenMapTiles vector schema (as served by OpenFreeMap, schema 3.x) to the
    /// provider-independent <see cref="MapFeature"/> model. Only layers needed by the POC are kept:
    /// landcover, landuse, park, water, waterway, transportation, building. Tunnels are dropped.
    /// Schema reference: https://openmaptiles.org/schema/ (CC-BY 4.0 design, BSD-3 code).
    /// </summary>
    public sealed class OpenMapTilesDecoder : IMapFeatureDecoder
    {
        /// <summary>Default storey height when a building has no render_height.</summary>
        public const float DefaultBuildingHeightMeters = 8f;

        /// <summary>Landuse classes treated as restricted/private for spawn safety (prototype list).</summary>
        public static readonly HashSet<string> RestrictedLanduseClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "military", "railway", "industrial", "quarry", "construction", "garages", "cemetery", "hospital",
            "school", "kindergarten", "college", "university", "prison", "dam"
        };

        public List<MapFeature> Decode(TileId tile, byte[] data)
        {
            var result = new List<MapFeature>();
            if (data == null || data.Length == 0)
            {
                return result;
            }

            foreach (MvtLayer layer in MvtReader.Read(Decompress(data)))
            {
                double extent = layer.Extent;
                foreach (MvtFeature f in layer.Features)
                {
                    MapFeature mf = Classify(tile, layer.Name, f);
                    if (mf == null)
                    {
                        continue;
                    }

                    if (!FillGeometry(mf, f, extent))
                    {
                        continue;
                    }

                    result.Add(mf);
                }
            }

            return result;
        }

        private static MapFeature Classify(TileId tile, string layer, MvtFeature f)
        {
            string cls = Str(f, "class");
            string brunnel = Str(f, "brunnel");
            switch (layer)
            {
                case "landcover":
                    if (f.Type != MvtGeometryType.Polygon)
                    {
                        return null;
                    }

                    return new MapFeature(tile, MapFeatureKind.Landcover) { Class = cls };
                case "landuse":
                    if (f.Type != MvtGeometryType.Polygon)
                    {
                        return null;
                    }

                    return new MapFeature(tile, MapFeatureKind.Landuse)
                    {
                        Class = cls,
                        IsPrivate = RestrictedLanduseClasses.Contains(cls)
                    };
                case "park":
                    if (f.Type != MvtGeometryType.Polygon)
                    {
                        return null;
                    }

                    return new MapFeature(tile, MapFeatureKind.Park) { Class = string.IsNullOrEmpty(cls) ? "park" : cls };
                case "water":
                    if (f.Type != MvtGeometryType.Polygon || brunnel == "tunnel")
                    {
                        return null;
                    }

                    return new MapFeature(tile, MapFeatureKind.Water) { Class = cls };
                case "waterway":
                    if (f.Type != MvtGeometryType.LineString || brunnel == "tunnel")
                    {
                        return null;
                    }

                    return new MapFeature(tile, MapFeatureKind.Waterway) { Class = cls };
                case "transportation":
                    return ClassifyTransportation(tile, f, cls, brunnel);
                case "building":
                    if (f.Type != MvtGeometryType.Polygon)
                    {
                        return null;
                    }

                    if (f.Properties.TryGetValue("hide_3d", out object hide) && hide is bool hb && hb)
                    {
                        return null;
                    }

                    float h = Num(f, "render_height", DefaultBuildingHeightMeters);
                    float minH = Num(f, "render_min_height", 0f);
                    return new MapFeature(tile, MapFeatureKind.Building)
                    {
                        Class = "building",
                        HeightMeters = Math.Max(h, minH + 1f),
                        MinHeightMeters = Math.Max(0f, minH)
                    };
                default:
                    return null;
            }
        }

        private static MapFeature ClassifyTransportation(TileId tile, MvtFeature f, string cls, string brunnel)
        {
            if (f.Type != MvtGeometryType.LineString || brunnel == "tunnel")
            {
                return null;
            }

            if (cls == "rail" || cls == "transit")
            {
                return new MapFeature(tile, MapFeatureKind.Rail) { Class = cls, IsBridge = brunnel == "bridge" };
            }

            RoadClass rc = RoadClassFor(cls);
            if (rc == RoadClass.None)
            {
                return null;
            }

            // OpenMapTiles resolves OSM access=private|no to access="no".
            string access = Str(f, "access");
            return new MapFeature(tile, MapFeatureKind.Road)
            {
                Class = cls,
                RoadClass = rc,
                IsPrivate = access == "no",
                IsBridge = brunnel == "bridge"
            };
        }

        /// <summary>
        /// Some hosts / HTTP stacks hand back the stored gzip body (Content-Encoding not undone);
        /// inflate it here so the reader only ever sees raw protobuf. Output is capped at 16 MB.
        /// </summary>
        public static byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length < 2 || data[0] != 0x1f || data[1] != 0x8b)
            {
                return data;
            }

            const int MaxInflatedBytes = 16 * 1024 * 1024;
            using (var input = new MemoryStream(data, false))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(data.Length * 4))
            {
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxInflatedBytes)
                    {
                        throw new MvtFormatException("Inflated tile exceeds 16 MB.");
                    }

                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        public static RoadClass RoadClassFor(string omtClass)
        {
            switch (omtClass)
            {
                case "motorway": return RoadClass.Motorway;
                case "trunk": return RoadClass.Trunk;
                case "primary": return RoadClass.Primary;
                case "secondary": return RoadClass.Secondary;
                case "tertiary": return RoadClass.Tertiary;
                case "minor":
                case "busway":
                case "raceway": return RoadClass.Minor;
                case "service": return RoadClass.Service;
                case "track": return RoadClass.Track;
                case "path": return RoadClass.Path;
                case "pedestrian": return RoadClass.Pedestrian;
                default: return RoadClass.None;
            }
        }

        private static bool FillGeometry(MapFeature mf, MvtFeature f, double extent)
        {
            if (f.Type == MvtGeometryType.LineString)
            {
                foreach (MvtRing part in f.Parts)
                {
                    if (part.Count >= 2)
                    {
                        mf.Lines.Add(ToPoints(part, extent));
                    }
                }

                return mf.Lines.Count > 0;
            }

            if (f.Type == MvtGeometryType.Polygon)
            {
                MapPolygon current = null;
                foreach (MvtRing ring in f.Parts)
                {
                    if (ring.Count < 3)
                    {
                        continue;
                    }

                    double area = ring.SignedArea();
                    if (area > 0)
                    {
                        current = new MapPolygon(ToPoints(ring, extent));
                        mf.Polygons.Add(current);
                    }
                    else if (area < 0 && current != null)
                    {
                        current.Holes.Add(ToPoints(ring, extent));
                    }
                }

                return mf.Polygons.Count > 0;
            }

            return false;
        }

        private static List<MapPoint> ToPoints(MvtRing ring, double extent)
        {
            var list = new List<MapPoint>(ring.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                list.Add(new MapPoint(ring.Xs[i] / extent, ring.Ys[i] / extent));
            }

            return list;
        }

        private static string Str(MvtFeature f, string key)
        {
            return f.Properties.TryGetValue(key, out object v) && v is string s ? s : string.Empty;
        }

        private static float Num(MvtFeature f, string key, float fallback)
        {
            return f.Properties.TryGetValue(key, out object v) && v is double d && !double.IsNaN(d) ? (float)d : fallback;
        }
    }
}
