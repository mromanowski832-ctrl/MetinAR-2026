using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class SpawnSafetyTests
{
    private static readonly GeoCoordinate Origin = new GeoCoordinate(50.0617, 19.9373);

    private static ProjectedFeature F(MapFeatureKind kind, Action<MapFeature> setup = null, double[] polygon = null, double[] line = null)
    {
        var mf = new MapFeature(new TileId(14, 9099, 5552), kind);
        setup?.Invoke(mf);
        // Build through the public projector to keep bounds consistent: express geometry directly in meters via a fake projected feature.
        var pf = new ProjectedFeature(mf);
        var include = typeof(ProjectedFeature).GetMethod("Include", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        if (polygon != null)
        {
            pf.Polygons.Add(new ProjectedPolygon(polygon));
            for (int i = 0; i < polygon.Length; i += 2) include.Invoke(pf, new object[] { polygon[i], polygon[i + 1] });
        }

        if (line != null)
        {
            pf.Lines.Add(line);
            for (int i = 0; i < line.Length; i += 2) include.Invoke(pf, new object[] { line[i], line[i + 1] });
        }

        return pf;
    }

    private static double[] Box(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

    private static List<ProjectedFeature> Scene() => new()
    {
        F(MapFeatureKind.Landuse, f => f.Class = "residential", polygon: Box(-500, -500, 500, 500)),
        F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Primary; f.Class = "primary"; }, line: new double[] { -500, 0, 500, 0 }),
        F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Path; f.Class = "path"; }, line: new double[] { 100, -500, 100, 500 }),
        F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Service; f.Class = "service"; f.IsPrivate = true; }, line: new double[] { -200, 100, -200, 300 }),
        F(MapFeatureKind.Water, f => f.Class = "river", polygon: Box(200, 200, 300, 300)),
        F(MapFeatureKind.Building, polygon: Box(-100, 50, -80, 70)),
        F(MapFeatureKind.Landuse, f => { f.Class = "military"; f.IsPrivate = true; }, polygon: Box(-400, -400, -300, -300)),
        F(MapFeatureKind.Rail, f => f.Class = "rail", line: new double[] { 400, -500, 400, 500 }),
    };

    private static SafetyResult At(double e, double n, double clearance = 0, SpawnSafetyConfig cfg = null) =>
        FeatureSpawnSafetyService.EvaluateLocal(Scene(), e, n, clearance, cfg ?? new SpawnSafetyConfig());

    [Fact]
    public void Classifies_Road_Water_Building_Private_Rail()
    {
        Assert.Equal(SafetyReason.Road, At(0, 10).Reason);          // 10 m from primary (buffer 15)
        Assert.Equal(SafetyReason.Water, At(250, 250).Reason);      // inside water
        Assert.Equal(SafetyReason.Water, At(250, 197).Reason);      // within 5 m water buffer
        Assert.Equal(SafetyReason.Building, At(-90, 60).Reason);    // inside footprint
        Assert.Equal(SafetyReason.Private, At(-203, 200).Reason);   // private road (buffer 6)
        Assert.Equal(SafetyReason.Private, At(-350, -350).Reason);  // restricted landuse
        Assert.Equal(SafetyReason.Rail, At(410, 100).Reason);
        Assert.All(new[] { At(0, 10), At(250, 250), At(-90, 60), At(-203, 200), At(410, 100) }, r => Assert.Equal(SafetyVerdict.Blocked, r.Verdict));
    }

    [Fact]
    public void Allows_OpenKnownSurface_AndFootpaths()
    {
        var r = At(50, 100);
        Assert.Equal(SafetyVerdict.Allowed, r.Verdict);
        Assert.True(r.IsSpawnAllowed);
        Assert.Equal(SafetyVerdict.Allowed, At(100, 100).Verdict); // on a footpath (buffer 0)
    }

    [Fact]
    public void ClearanceAndBuffers_AreConfigurable()
    {
        Assert.Equal(SafetyVerdict.Allowed, At(0, 20).Verdict);
        Assert.Equal(SafetyReason.Road, At(0, 20, clearance: 6).Reason); // GPS accuracy margin
        var cfg = new SpawnSafetyConfig { PrimaryBuffer = 25 };
        Assert.Equal(SafetyReason.Road, At(0, 20, cfg: cfg).Reason);
        var noPrimary = new SpawnSafetyConfig { PrimaryBuffer = 0 };
        Assert.Equal(SafetyVerdict.Allowed, At(0, 1, cfg: noPrimary).Verdict);
    }

    [Fact]
    public void UnclassifiedSurface_IsUnknown_AndDenied()
    {
        var r = At(800, 800);
        Assert.Equal(SafetyVerdict.Unknown, r.Verdict);
        Assert.Equal(SafetyReason.UnclassifiedSurface, r.Reason);
        Assert.False(r.IsSpawnAllowed);
        var lenient = new SpawnSafetyConfig { RequireKnownSurface = false };
        Assert.Equal(SafetyVerdict.Allowed, At(800, 800, cfg: lenient).Verdict);
    }

    [Fact]
    public void Service_NoFrameOrOutsideCoverage_IsUnknownDefaultDeny()
    {
        var svc = new FeatureSpawnSafetyService();
        Assert.Equal(SafetyVerdict.Unknown, svc.Evaluate(Origin, 0).Verdict);

        var frame = new LocalTangentFrame(Origin);
        svc.SetFrame(frame);
        var coverage = new GeoBounds(Origin.Longitude - 0.005, Origin.Latitude - 0.005, Origin.Longitude + 0.005, Origin.Latitude + 0.005);
        svc.SetTile(new TileId(14, 9099, 5552), coverage, Scene());
        var far = GeoMath.OffsetMeters(Origin, 5000, 0);
        var res = svc.Evaluate(far, 0);
        Assert.Equal(SafetyVerdict.Unknown, res.Verdict);
        Assert.Equal(SafetyReason.NoData, res.Reason);
        Assert.False(res.IsSpawnAllowed);

        // Inside coverage, 10 m north of the primary road through the origin → Road.
        Assert.Equal(SafetyReason.Road, svc.Evaluate(GeoMath.OffsetMeters(Origin, 10, 0), 0).Reason);

        // TryFindSafePoint moves off the road onto allowed residential surface.
        Assert.True(svc.TryFindSafePoint(Origin, 60, 0, out var safe));
        Assert.True(svc.Evaluate(safe, 0).IsSpawnAllowed);
        Assert.True(GeoMath.DistanceMeters(Origin, safe) >= 15);

        svc.RemoveTile(new TileId(14, 9099, 5552));
        Assert.Equal(SafetyVerdict.Unknown, svc.Evaluate(Origin, 0).Verdict);
    }

    [Fact]
    public void ResultText_NeverContainsCoordinates()
    {
        string s = At(0, 10).ToString();
        Assert.Equal("Blocked(Road:primary)", s);
    }
}
