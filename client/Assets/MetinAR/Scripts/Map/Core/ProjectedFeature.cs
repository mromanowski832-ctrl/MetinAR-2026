using System;
using System.Collections.Generic;

namespace MetinAR.Map
{
    /// <summary>Polygon in local meters: arrays are interleaved (east, north) pairs.</summary>
    public sealed class ProjectedPolygon
    {
        public ProjectedPolygon(double[] exterior)
        {
            Exterior = exterior;
        }

        public double[] Exterior { get; }

        public List<double[]> Holes { get; } = new List<double[]>();
    }

    /// <summary>
    /// A <see cref="MapFeature"/> projected into the shared WorldMapOrigin local-meter frame
    /// (east = +X, north = +Z). Used by both the mesh builder and the spawn-safety service.
    /// </summary>
    public sealed class ProjectedFeature
    {
        public ProjectedFeature(MapFeature source)
        {
            Source = source;
        }

        public MapFeature Source { get; }

        public MapFeatureKind Kind => Source.Kind;

        public List<double[]> Lines { get; } = new List<double[]>();

        public List<ProjectedPolygon> Polygons { get; } = new List<ProjectedPolygon>();

        public double MinEast { get; private set; } = double.MaxValue;

        public double MinNorth { get; private set; } = double.MaxValue;

        public double MaxEast { get; private set; } = double.MinValue;

        public double MaxNorth { get; private set; } = double.MinValue;

        public bool HasGeometry => Lines.Count > 0 || Polygons.Count > 0;

        internal void Include(double east, double north)
        {
            MinEast = Math.Min(MinEast, east);
            MaxEast = Math.Max(MaxEast, east);
            MinNorth = Math.Min(MinNorth, north);
            MaxNorth = Math.Max(MaxNorth, north);
        }

        public bool BoundsIntersect(double minEast, double minNorth, double maxEast, double maxNorth)
        {
            return !(minEast > MaxEast || maxEast < MinEast || minNorth > MaxNorth || maxNorth < MinNorth);
        }
    }

    /// <summary>Projects tile-normalized features into a <see cref="LocalTangentFrame"/>.</summary>
    public static class FeatureProjector
    {
        public static ProjectedFeature Project(MapFeature feature, LocalTangentFrame frame)
        {
            var result = new ProjectedFeature(feature);
            foreach (List<MapPoint> line in feature.Lines)
            {
                if (line.Count >= 2)
                {
                    result.Lines.Add(ProjectRing(feature.SourceTile, line, frame, result));
                }
            }

            foreach (MapPolygon polygon in feature.Polygons)
            {
                if (polygon.Exterior.Count < 3)
                {
                    continue;
                }

                var p = new ProjectedPolygon(ProjectRing(feature.SourceTile, polygon.Exterior, frame, result));
                foreach (List<MapPoint> hole in polygon.Holes)
                {
                    if (hole.Count >= 3)
                    {
                        p.Holes.Add(ProjectRing(feature.SourceTile, hole, frame, result));
                    }
                }

                result.Polygons.Add(p);
            }

            return result;
        }

        /// <summary>
        /// Projects all features and drops every line / polygon part whose own bounds miss the given local-meter
        /// window (sources can merge many buildings into one multi-polygon, so culling is per part).
        /// </summary>
        public static List<ProjectedFeature> ProjectAndCull(
            IReadOnlyList<MapFeature> features,
            LocalTangentFrame frame,
            double minEast,
            double minNorth,
            double maxEast,
            double maxNorth)
        {
            var list = new List<ProjectedFeature>(features.Count);
            foreach (MapFeature f in features)
            {
                ProjectedFeature p = Project(f, frame);
                if (!p.HasGeometry || !p.BoundsIntersect(minEast, minNorth, maxEast, maxNorth))
                {
                    continue;
                }

                var kept = new ProjectedFeature(f);
                foreach (double[] line in p.Lines)
                {
                    if (RingIntersects(line, minEast, minNorth, maxEast, maxNorth))
                    {
                        kept.Lines.Add(line);
                        IncludeRing(kept, line);
                    }
                }

                foreach (ProjectedPolygon poly in p.Polygons)
                {
                    if (RingIntersects(poly.Exterior, minEast, minNorth, maxEast, maxNorth))
                    {
                        kept.Polygons.Add(poly);
                        IncludeRing(kept, poly.Exterior);
                    }
                }

                if (kept.HasGeometry)
                {
                    list.Add(kept);
                }
            }

            return list;
        }

        private static bool RingIntersects(double[] ring, double minEast, double minNorth, double maxEast, double maxNorth)
        {
            double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
            for (int i = 0; i + 1 < ring.Length; i += 2)
            {
                a = Math.Min(a, ring[i]);
                c = Math.Max(c, ring[i]);
                b = Math.Min(b, ring[i + 1]);
                d = Math.Max(d, ring[i + 1]);
            }

            return !(minEast > c || maxEast < a || minNorth > d || maxNorth < b);
        }

        private static void IncludeRing(ProjectedFeature f, double[] ring)
        {
            for (int i = 0; i + 1 < ring.Length; i += 2)
            {
                f.Include(ring[i], ring[i + 1]);
            }
        }

        private static double[] ProjectRing(TileId tile, List<MapPoint> points, LocalTangentFrame frame, ProjectedFeature bounds)
        {
            var arr = new double[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                WebMercator.TileNormalizedToGeo(tile, points[i].U, points[i].V, out double lat, out double lon);
                frame.ToLocalMeters(lat, lon, out double e, out double n);
                arr[i * 2] = e;
                arr[i * 2 + 1] = n;
                bounds.Include(e, n);
            }

            return arr;
        }
    }
}
