using MetinAR.Location;
using MetinAR.Map;

namespace MapCore.Tests;

/// <summary>
/// Synthetic scene (local metres around a synthetic origin, not a real place) evaluated through the
/// production <see cref="FeatureSpawnSafetyService.EvaluateLocal"/> classifier, exposed as an ISpawnSafetyService.
/// </summary>
internal sealed class Stage1TestScene : ISpawnSafetyService
{
    public static readonly GeoCoordinate Origin = new GeoCoordinate(52.0, 20.0);
    private readonly LocalTangentFrame _frame = new LocalTangentFrame(Origin);
    private readonly List<ProjectedFeature> _features;
    private readonly double _coverageHalfSize;

    public Stage1TestScene(List<ProjectedFeature> features = null, double coverageHalfSize = 500)
    {
        _features = features ?? Default();
        _coverageHalfSize = coverageHalfSize;
    }

    public SpawnSafetyConfig Config { get; } = new SpawnSafetyConfig();

    public int Evaluations { get; private set; }

    public GeoCoordinate At(double east, double north) => _frame.ToGeo(east, north);

    public void ToLocal(GeoCoordinate c, out double e, out double n) => _frame.ToLocalMeters(c, out e, out n);

    public SafetyResult Evaluate(GeoCoordinate point, double clearanceMeters)
    {
        Evaluations++;
        _frame.ToLocalMeters(point, out double e, out double n);
        if (Math.Abs(e) > _coverageHalfSize || Math.Abs(n) > _coverageHalfSize)
        {
            return new SafetyResult(SafetyVerdict.Unknown, SafetyReason.NoData, "outside-coverage");
        }

        return FeatureSpawnSafetyService.EvaluateLocal(_features, e, n, clearanceMeters, Config);
    }

    public bool TryFindSafePoint(GeoCoordinate near, double searchRadiusMeters, double clearanceMeters, out GeoCoordinate safePoint)
    {
        safePoint = near;
        return Evaluate(near, clearanceMeters).IsSpawnAllowed;
    }

    public static ProjectedFeature F(MapFeatureKind kind, Action<MapFeature> setup = null, double[] polygon = null, double[] line = null)
    {
        var mf = new MapFeature(new TileId(14, 9099, 5552), kind);
        setup?.Invoke(mf);
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

    public static double[] Box(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

    /// <summary>
    /// Park everywhere except: primary road along north=0, rail along east=400, river polygon 200..300,
    /// building -100..-80 / 50..70, industrial landuse -400..-300, unclassified hole (no landcover) 300..500 north.
    /// </summary>
    public static List<ProjectedFeature> Default() => new()
    {
        F(MapFeatureKind.Park, f => f.Class = "park", polygon: Box(-500, -500, 500, 300)),
        F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Primary; f.Class = "primary"; }, line: new double[] { -500, 0, 500, 0 }),
        F(MapFeatureKind.Rail, f => f.Class = "rail", line: new double[] { 400, -500, 400, 500 }),
        F(MapFeatureKind.Water, f => f.Class = "river", polygon: Box(200, 100, 300, 200)),
        F(MapFeatureKind.Building, polygon: Box(-100, 50, -80, 70)),
        F(MapFeatureKind.Landuse, f => { f.Class = "industrial"; f.IsPrivate = true; }, polygon: Box(-400, -400, -300, -300)),
    };
}
