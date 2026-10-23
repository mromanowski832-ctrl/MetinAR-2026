using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class GeometryTests
{
    private static double Area(double[] v, List<int> tris)
    {
        double a = 0;
        for (int i = 0; i < tris.Count; i += 3)
        {
            int p = tris[i], q = tris[i + 1], r = tris[i + 2];
            a += ((v[q * 2] - v[p * 2]) * (v[r * 2 + 1] - v[p * 2 + 1]) - (v[q * 2 + 1] - v[p * 2 + 1]) * (v[r * 2] - v[p * 2])) / 2;
        }

        return a;
    }

    [Fact]
    public void Triangulate_Square_BothWindings()
    {
        var ccw = new double[] { 0, 0, 10, 0, 10, 10, 0, 10 };
        var cw = new double[] { 0, 0, 0, 10, 10, 10, 10, 0 };
        foreach (var ring in new[] { ccw, cw })
        {
            var t = Triangulator.Triangulate(ring, null, out var v);
            Assert.Equal(6, t.Count);
            Assert.Equal(100, Area(v, t), 6); // CCW output, full area
        }
    }

    [Fact]
    public void Triangulate_ConcaveAndWithHole_PreservesArea()
    {
        var l = new double[] { 0, 0, 20, 0, 20, 10, 10, 10, 10, 20, 0, 20 };
        var tl = Triangulator.Triangulate(l, null, out var vl);
        Assert.Equal(300, Area(vl, tl), 6);
        Assert.Equal(4 * 3, tl.Count);

        var outer = new double[] { 0, 0, 30, 0, 30, 30, 0, 30 };
        var hole = new double[] { 10, 10, 20, 10, 20, 20, 10, 20 };
        var th = Triangulator.Triangulate(outer, new[] { hole }, out var vh);
        Assert.Equal(900 - 100, Area(vh, th), 6);
    }

    [Fact]
    public void Triangulate_Degenerate_DoesNotThrow()
    {
        Assert.Empty(Triangulator.Triangulate(new double[] { 0, 0, 1, 1, 2, 2 }, null, out _));
        Assert.Empty(Triangulator.Triangulate(new double[] { 0, 0, 1, 1 }, null, out _));
    }

    private static ProjectedFeature Feature(MapFeatureKind kind, Action<MapFeature> setup = null, double[] polygon = null, double[] line = null)
    {
        var mf = new MapFeature(new TileId(14, 0, 0), kind);
        setup?.Invoke(mf);
        var pf = new ProjectedFeature(mf);
        if (polygon != null)
        {
            pf.Polygons.Add(new ProjectedPolygon(polygon));
            for (int i = 0; i < polygon.Length; i += 2) Include(pf, polygon[i], polygon[i + 1]);
        }

        if (line != null)
        {
            pf.Lines.Add(line);
            for (int i = 0; i < line.Length; i += 2) Include(pf, line[i], line[i + 1]);
        }

        return pf;
    }

    private static void Include(ProjectedFeature f, double e, double n) =>
        typeof(ProjectedFeature).GetMethod("Include", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(f, new object[] { e, n });

    private static float UnityNormalY(MapMeshData m, int tri)
    {
        int a = m.Indices[tri * 3], b = m.Indices[tri * 3 + 1], c = m.Indices[tri * 3 + 2];
        float ax = m.Positions[b * 3] - m.Positions[a * 3], az = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
        float bx = m.Positions[c * 3] - m.Positions[a * 3], bz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
        return az * bx - ax * bz; // y of cross(b - a, c - a): Unity front-face normal
    }

    [Fact]
    public void MeshBuilder_FillsAndRibbonsFaceUp_BuildingWallsFaceOutward()
    {
        var style = new MapMeshStyle();
        var park = Feature(MapFeatureKind.Park, polygon: new double[] { 0, 0, 10, 0, 10, 10, 0, 10 });
        var road = Feature(MapFeatureKind.Road, f => f.RoadClass = RoadClass.Primary, line: new double[] { 0, 0, 50, 0, 50, 40 });
        var bld = Feature(MapFeatureKind.Building, f => { f.HeightMeters = 10; }, polygon: new double[] { 0, 0, 0, 10, 10, 10, 10, 0 });
        var layers = MapMeshBuilder.Build(new[] { park, road, bld }, style);

        var parkMesh = layers[MapRenderLayer.Park];
        Assert.Equal(2, parkMesh.TriangleCount);
        for (int t = 0; t < parkMesh.TriangleCount; t++) Assert.True(UnityNormalY(parkMesh, t) > 0);

        var roadMesh = layers[MapRenderLayer.RoadMajor];
        Assert.Equal(2 * 2 + 6, roadMesh.TriangleCount); // two segment quads + one hexagon joint at the 90° bend
        for (int t = 0; t < roadMesh.TriangleCount; t++) Assert.True(UnityNormalY(roadMesh, t) > 0, "road tri " + t);
        Assert.All(Enumerable.Range(0, roadMesh.VertexCount), i => Assert.Equal(style.RoadMajorY, roadMesh.Positions[i * 3 + 1], 5));

        var b = layers[MapRenderLayer.Building];
        Assert.Equal(2 + 4 * 2, b.TriangleCount); // roof + 4 walls
        for (int t = 0; t < 2; t++) Assert.True(UnityNormalY(b, t) > 0);
        Assert.Equal(10 * style.BuildingHeightScale, b.Positions[1], 4);
        // Walls: geometric normal (cross) agrees with the stored outward normal.
        for (int t = 2; t < b.TriangleCount; t++)
        {
            int i0 = b.Indices[t * 3], i1 = b.Indices[t * 3 + 1], i2 = b.Indices[t * 3 + 2];
            var p = b.Positions;
            float ax = p[i1 * 3] - p[i0 * 3], ay = p[i1 * 3 + 1] - p[i0 * 3 + 1], az = p[i1 * 3 + 2] - p[i0 * 3 + 2];
            float bx = p[i2 * 3] - p[i0 * 3], by = p[i2 * 3 + 1] - p[i0 * 3 + 1], bz = p[i2 * 3 + 2] - p[i0 * 3 + 2];
            float cx = ay * bz - az * by, cz = ax * by - ay * bx;
            float nx = b.Normals[i0 * 3], nz = b.Normals[i0 * 3 + 2];
            Assert.True(cx * nx + cz * nz > 0, "wall tri " + t);
            // outward from the square centre (5, 5)
            float mx = (p[i0 * 3] + p[i1 * 3] + p[i2 * 3]) / 3 - 5, mz = (p[i0 * 3 + 2] + p[i1 * 3 + 2] + p[i2 * 3 + 2]) / 3 - 5;
            Assert.True(mx * nx + mz * nz > 0, "outward " + t);
        }
    }

    [Fact]
    public void Projection_AlignsTileGeometryWithLocalFrame()
    {
        var tile = new TileId(16, 36397, 22208);
        GeoBounds b = WebMercator.Bounds(tile);
        var origin = new GeoCoordinate(b.North, b.West);
        var frame = new LocalTangentFrame(origin);
        var mf = new MapFeature(tile, MapFeatureKind.Road);
        mf.Lines.Add(new List<MapPoint> { new MapPoint(0, 0), new MapPoint(1, 0), new MapPoint(1, 1) });
        var p = FeatureProjector.Project(mf, frame);
        double[] l = p.Lines[0];
        Assert.Equal(0, l[0], 6);
        Assert.Equal(0, l[1], 6);
        double width = WebMercator.TileWidthMeters(16, b.North);
        Assert.Equal(width, l[2], 0); // north-east corner ≈ one tile east
        Assert.True(l[5] < -300); // south-east corner is south (negative north)

        var culled = FeatureProjector.ProjectAndCull(new[] { mf }, frame, 5000, 5000, 6000, 6000);
        Assert.Empty(culled);
    }
}
