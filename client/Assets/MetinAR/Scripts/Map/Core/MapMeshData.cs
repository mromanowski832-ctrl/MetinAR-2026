using System;
using System.Collections.Generic;

namespace MetinAR.Map
{
    /// <summary>Render layers (one mesh + material each per tile). Order = draw/elevation order.</summary>
    public enum MapRenderLayer
    {
        Landcover = 0,
        Landuse = 1,
        Park = 2,
        Water = 3,
        RoadMinor = 4,
        RoadMajor = 5,
        Path = 6,
        Rail = 7,
        Building = 8
    }

    /// <summary>Engine-agnostic mesh buffers (x = east, y = up, z = north, meters).</summary>
    public sealed class MapMeshData
    {
        public List<float> Positions { get; } = new List<float>();

        public List<float> Normals { get; } = new List<float>();

        public List<int> Indices { get; } = new List<int>();

        public int VertexCount => Positions.Count / 3;

        public int TriangleCount => Indices.Count / 3;

        public int AddVertex(double x, double y, double z, float nx, float ny, float nz)
        {
            Positions.Add((float)x);
            Positions.Add((float)y);
            Positions.Add((float)z);
            Normals.Add(nx);
            Normals.Add(ny);
            Normals.Add(nz);
            return VertexCount - 1;
        }
    }

    /// <summary>Widths/heights used by <see cref="MapMeshBuilder"/>. Technical POC styling only.</summary>
    public sealed class MapMeshStyle
    {
        public float LandcoverY { get; set; } = 0.00f;
        public float LanduseY { get; set; } = 0.02f;
        public float ParkY { get; set; } = 0.04f;
        public float WaterY { get; set; } = 0.06f;
        public float RoadMinorY { get; set; } = 0.08f;
        public float RoadMajorY { get; set; } = 0.10f;
        public float PathY { get; set; } = 0.12f;
        public float RailY { get; set; } = 0.13f;
        public float BridgeLift { get; set; } = 0.05f;

        public float WaterwayWidth { get; set; } = 4f;
        public float RailWidth { get; set; } = 2.5f;

        /// <summary>Buildings taller than this are clamped so they don't wall off the angled view.</summary>
        public float MaxBuildingHeight { get; set; } = 30f;

        /// <summary>Vertical exaggeration &lt; 1 keeps the game look readable (POC value).</summary>
        public float BuildingHeightScale { get; set; } = 0.6f;

        public float RoadWidth(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: return 14f;
                case RoadClass.Trunk: return 12f;
                case RoadClass.Primary: return 10f;
                case RoadClass.Secondary: return 8.5f;
                case RoadClass.Tertiary: return 7f;
                case RoadClass.Minor: return 5.5f;
                case RoadClass.Service: return 3.5f;
                case RoadClass.Track: return 3f;
                case RoadClass.Path: return 1.6f;
                case RoadClass.Pedestrian: return 4f;
                default: return 4f;
            }
        }

        public static MapRenderLayer RoadLayer(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway:
                case RoadClass.Trunk:
                case RoadClass.Primary:
                case RoadClass.Secondary:
                case RoadClass.Tertiary:
                    return MapRenderLayer.RoadMajor;
                case RoadClass.Path:
                case RoadClass.Pedestrian:
                case RoadClass.Track:
                    return MapRenderLayer.Path;
                default:
                    return MapRenderLayer.RoadMinor;
            }
        }
    }

    /// <summary>
    /// Pure mesh generation from projected features: triangulated fills for land/water, flat ribbons for
    /// roads/rail/waterways (per-segment quads + joint caps), and extruded buildings (roof + walls).
    /// Thread-safe given separate outputs; runs off the Unity main thread.
    /// </summary>
    public static class MapMeshBuilder
    {
        public static Dictionary<MapRenderLayer, MapMeshData> Build(IReadOnlyList<ProjectedFeature> features, MapMeshStyle style)
        {
            var layers = new Dictionary<MapRenderLayer, MapMeshData>();
            foreach (ProjectedFeature f in features)
            {
                switch (f.Kind)
                {
                    case MapFeatureKind.Landcover:
                        AddFills(Get(layers, MapRenderLayer.Landcover), f, style.LandcoverY);
                        break;
                    case MapFeatureKind.Landuse:
                        AddFills(Get(layers, MapRenderLayer.Landuse), f, style.LanduseY);
                        break;
                    case MapFeatureKind.Park:
                        AddFills(Get(layers, MapRenderLayer.Park), f, style.ParkY);
                        break;
                    case MapFeatureKind.Water:
                        AddFills(Get(layers, MapRenderLayer.Water), f, style.WaterY);
                        break;
                    case MapFeatureKind.Waterway:
                        AddRibbons(Get(layers, MapRenderLayer.Water), f, style.WaterwayWidth, style.WaterY);
                        break;
                    case MapFeatureKind.Road:
                        MapRenderLayer rl = MapMeshStyle.RoadLayer(f.Source.RoadClass);
                        float y = rl == MapRenderLayer.RoadMajor ? style.RoadMajorY : rl == MapRenderLayer.Path ? style.PathY : style.RoadMinorY;
                        AddRibbons(Get(layers, rl), f, style.RoadWidth(f.Source.RoadClass), y + (f.Source.IsBridge ? style.BridgeLift : 0f));
                        break;
                    case MapFeatureKind.Rail:
                        AddRibbons(Get(layers, MapRenderLayer.Rail), f, style.RailWidth, style.RailY + (f.Source.IsBridge ? style.BridgeLift : 0f));
                        break;
                    case MapFeatureKind.Building:
                        AddBuilding(Get(layers, MapRenderLayer.Building), f, style);
                        break;
                }
            }

            return layers;
        }

        private static MapMeshData Get(Dictionary<MapRenderLayer, MapMeshData> layers, MapRenderLayer layer)
        {
            if (!layers.TryGetValue(layer, out MapMeshData d))
            {
                d = new MapMeshData();
                layers[layer] = d;
            }

            return d;
        }

        public static void AddFills(MapMeshData mesh, ProjectedFeature f, float y)
        {
            foreach (ProjectedPolygon p in f.Polygons)
            {
                AddFill(mesh, p, y);
            }
        }

        public static int AddFill(MapMeshData mesh, ProjectedPolygon p, float y)
        {
            List<int> tris = Triangulator.Triangulate(p.Exterior, p.Holes, out double[] verts);
            if (tris.Count == 0)
            {
                return 0;
            }

            int baseIndex = mesh.VertexCount;
            for (int i = 0; i < verts.Length; i += 2)
            {
                mesh.AddVertex(verts[i], y, verts[i + 1], 0f, 1f, 0f);
            }

            // Triangulator emits CCW in (east, north). In Unity (left-handed, front face = clockwise seen by the
            // viewer, normal = cross(b - a, c - a)) a CCW (east, north) triangle faces DOWN, so swap two indices.
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                mesh.Indices.Add(baseIndex + tris[i]);
                mesh.Indices.Add(baseIndex + tris[i + 2]);
                mesh.Indices.Add(baseIndex + tris[i + 1]);
            }

            return tris.Count / 3;
        }

        public static void AddRibbons(MapMeshData mesh, ProjectedFeature f, float width, float y)
        {
            float half = width * 0.5f;
            foreach (double[] line in f.Lines)
            {
                for (int i = 0; i + 3 < line.Length; i += 2)
                {
                    AddSegmentQuad(mesh, line[i], line[i + 1], line[i + 2], line[i + 3], half, y);
                }

                // Round-ish caps only at joints that actually bend (> ~15°) to hide gaps between segment quads.
                for (int i = 2; i + 1 < line.Length - 2; i += 2)
                {
                    if (BendsSharply(line, i))
                    {
                        AddJoint(mesh, line[i], line[i + 1], half, y);
                    }
                }
            }
        }

        private static bool BendsSharply(double[] line, int i)
        {
            double ax = line[i] - line[i - 2], az = line[i + 1] - line[i - 1];
            double bx = line[i + 2] - line[i], bz = line[i + 3] - line[i + 1];
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-6 || lb < 1e-6)
            {
                return false;
            }

            return (ax * bx + az * bz) / (la * lb) < 0.966; // cos 15°
        }

        private static void AddSegmentQuad(MapMeshData mesh, double ax, double az, double bx, double bz, float half, float y)
        {
            double dx = bx - ax;
            double dz = bz - az;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6)
            {
                return;
            }

            // Left normal in (east, north): (-dz, dx).
            double nx = -dz / len * half;
            double nz = dx / len * half;
            int v0 = mesh.AddVertex(ax + nx, y, az + nz, 0f, 1f, 0f);
            int v1 = mesh.AddVertex(bx + nx, y, bz + nz, 0f, 1f, 0f);
            int v2 = mesh.AddVertex(bx - nx, y, bz - nz, 0f, 1f, 0f);
            int v3 = mesh.AddVertex(ax - nx, y, az - nz, 0f, 1f, 0f);
            AddQuadUp(mesh, v0, v1, v2, v3);
        }

        private static void AddJoint(MapMeshData mesh, double cx, double cz, float half, float y)
        {
            // Octagon approximating a round join.
            int center = mesh.AddVertex(cx, y, cz, 0f, 1f, 0f);
            const int segments = 6;
            int first = mesh.VertexCount;
            for (int k = 0; k < segments; k++)
            {
                double a = 2 * Math.PI * k / segments;
                mesh.AddVertex(cx + Math.Cos(a) * half, y, cz + Math.Sin(a) * half, 0f, 1f, 0f);
            }

            for (int k = 0; k < segments; k++)
            {
                int a = first + k;
                int b = first + (k + 1) % segments;
                // Angles increase counter-clockwise in (east, north); (center, b, a) faces up in Unity.
                mesh.Indices.Add(center);
                mesh.Indices.Add(b);
                mesh.Indices.Add(a);
            }
        }

        /// <summary>
        /// Adds a quad whose four vertices are given in order around its perimeter; picks the triangle winding
        /// that faces up (+Y) in Unity regardless of the input direction.
        /// </summary>
        private static void AddQuadUp(MapMeshData mesh, int v0, int v1, int v2, int v3)
        {
            // CCW in (east, north) ⇔ positive 2D cross; such a triangle faces down in Unity, so it is reversed.
            float x0 = mesh.Positions[v0 * 3], z0 = mesh.Positions[v0 * 3 + 2];
            float x1 = mesh.Positions[v1 * 3], z1 = mesh.Positions[v1 * 3 + 2];
            float x2 = mesh.Positions[v2 * 3], z2 = mesh.Positions[v2 * 3 + 2];
            double cross = (x1 - x0) * (z2 - z0) - (z1 - z0) * (x2 - x0);
            if (cross < 0)
            {
                mesh.Indices.Add(v0); mesh.Indices.Add(v1); mesh.Indices.Add(v2);
                mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v3);
            }
            else
            {
                mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v1);
                mesh.Indices.Add(v0); mesh.Indices.Add(v3); mesh.Indices.Add(v2);
            }
        }

        public static void AddBuilding(MapMeshData mesh, ProjectedFeature f, MapMeshStyle style)
        {
            float top = Math.Min(style.MaxBuildingHeight, f.Source.HeightMeters * style.BuildingHeightScale);
            float bottom = Math.Min(top - 0.5f, f.Source.MinHeightMeters * style.BuildingHeightScale);
            if (bottom < 0f)
            {
                bottom = 0f;
            }

            foreach (ProjectedPolygon p in f.Polygons)
            {
                AddFill(mesh, p, top);
                AddWalls(mesh, p.Exterior, bottom, top);
                foreach (double[] hole in p.Holes)
                {
                    AddWalls(mesh, hole, bottom, top);
                }
            }
        }

        /// <summary>Vertical walls along a ring; each wall faces outward from the ring's interior.</summary>
        public static void AddWalls(MapMeshData mesh, double[] ring, float bottom, float top)
        {
            int n = ring.Length / 2;
            if (n < 3)
            {
                return;
            }

            bool ccw = Geometry2D.SignedArea(ring) > 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double ax = ring[i * 2], az = ring[i * 2 + 1];
                double bx = ring[j * 2], bz = ring[j * 2 + 1];
                double dx = bx - ax, dz = bz - az;
                double len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6)
                {
                    continue;
                }

                // Outward normal: for a CCW ring (east, north) the interior is on the left, so outward = right = (dz, -dx).
                float nx = (float)(ccw ? dz / len : -dz / len);
                float nz = (float)(ccw ? -dx / len : dx / len);
                int v0 = mesh.AddVertex(ax, bottom, az, nx, 0f, nz);
                int v1 = mesh.AddVertex(bx, bottom, bz, nx, 0f, nz);
                int v2 = mesh.AddVertex(bx, top, bz, nx, 0f, nz);
                int v3 = mesh.AddVertex(ax, top, az, nx, 0f, nz);
                AddWallQuad(mesh, v0, v1, v2, v3, nx, nz);
            }
        }

        private static void AddWallQuad(MapMeshData mesh, int v0, int v1, int v2, int v3, float nx, float nz)
        {
            // Geometric normal of triangle (v0, v1, v2) in Unity's left-handed convention is cross(v1 - v0, v2 - v0)
            // for clockwise front faces → compare with the desired outward normal and flip when needed.
            float ax = mesh.Positions[v1 * 3] - mesh.Positions[v0 * 3];
            float ay = mesh.Positions[v1 * 3 + 1] - mesh.Positions[v0 * 3 + 1];
            float az = mesh.Positions[v1 * 3 + 2] - mesh.Positions[v0 * 3 + 2];
            float bx = mesh.Positions[v2 * 3] - mesh.Positions[v0 * 3];
            float by = mesh.Positions[v2 * 3 + 1] - mesh.Positions[v0 * 3 + 1];
            float bz = mesh.Positions[v2 * 3 + 2] - mesh.Positions[v0 * 3 + 2];
            float cx = ay * bz - az * by;
            float cz = ax * by - ay * bx;
            if (cx * nx + cz * nz >= 0)
            {
                mesh.Indices.Add(v0); mesh.Indices.Add(v1); mesh.Indices.Add(v2);
                mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v3);
            }
            else
            {
                mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v1);
                mesh.Indices.Add(v0); mesh.Indices.Add(v3); mesh.Indices.Add(v2);
            }
        }
    }
}
