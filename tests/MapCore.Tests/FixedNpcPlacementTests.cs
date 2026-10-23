using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

/// <summary>Synthetic coordinates only (Stage1TestScene origin); no real player or place coordinates.</summary>
public class FixedNpcPlacementTests
{
    private const double Big = 5000;

    private static Stage1TestScene OpenPark(params ProjectedFeature[] extra)
    {
        var features = new List<ProjectedFeature>
        {
            Stage1TestScene.F(MapFeatureKind.Park, f => f.Class = "park", polygon: Stage1TestScene.Box(-Big, -Big, Big, Big)),
        };
        features.AddRange(extra);
        return new Stage1TestScene(features, coverageHalfSize: Big);
    }

    private static EncounterSitePolicy Policy(Stage1TestScene scene) => new EncounterSitePolicy(scene);

    [Fact]
    public void ManagerDecision_DefaultCellIs500m()
    {
        Assert.Equal(500, new FixedNpcPlacement("wandering_merchant").CellSizeMeters);
    }

    [Fact]
    public void SameNpcSameCell_SameSite_AcrossInstances()
    {
        var policy = Policy(OpenPark());
        var a = new FixedNpcPlacement("wandering_merchant");
        var b = new FixedNpcPlacement("wandering_merchant");
        var p = Stage1TestScene.Origin;
        Assert.True(a.TryResolveNearest(p, policy, out var sa));
        Assert.True(b.TryResolveNearest(p, policy, out var sb)); // another session / device
        Assert.Equal(sa.Coordinate.Latitude, sb.Coordinate.Latitude, 12);
        Assert.Equal(sa.Coordinate.Longitude, sb.Coordinate.Longitude, 12);
        Assert.False(sa.IsOverride);

        // The site of a given cell never depends on where inside the 3x3 neighbourhood the player stands.
        Assert.True(b.TryResolveNearest(GeoMath.OffsetMeters(p, 120, -90), policy, out var moved));
        if (moved.CellRow == sa.CellRow && moved.CellColumn == sa.CellColumn)
        {
            Assert.Equal(sa.Coordinate.Latitude, moved.Coordinate.Latitude, 12);
        }

        Assert.True(policy.TryPlace(a.NominalSite(moved.CellRow, moved.CellColumn), a.SafeSearchRadiusMeters, out var expected, out _));
        Assert.Equal(expected.Latitude, moved.Coordinate.Latitude, 12);
        Assert.Equal(expected.Longitude, moved.Coordinate.Longitude, 12);
    }

    [Fact]
    public void NominalSite_LiesInsideItsCell_AwayFromEdges()
    {
        var p = new FixedNpcPlacement("wandering_merchant", 500);
        p.GetCell(Stage1TestScene.Origin, out long row, out long col);
        for (long r = row - 3; r <= row + 3; r++)
        {
            for (long c = col - 3; c <= col + 3; c++)
            {
                GeoCoordinate s = p.NominalSite(r, c);
                p.GetCell(s, out long rr, out long cc);
                Assert.Equal(r, rr);
                Assert.Equal(c, cc);
            }
        }
    }

    [Fact]
    public void DifferentNpcIds_GetDifferentSites()
    {
        var a = new FixedNpcPlacement("wandering_merchant");
        var b = new FixedNpcPlacement("blacksmith");
        a.GetCell(Stage1TestScene.Origin, out long r, out long c);
        Assert.NotEqual(a.NominalSite(r, c).Latitude, b.NominalSite(r, c).Latitude);
    }

    [Fact]
    public void Seed_IsDeterministic_AndChangesTheSite()
    {
        var a = new FixedNpcPlacement("wandering_merchant", worldSeed: "season-1");
        var a2 = new FixedNpcPlacement("wandering_merchant", worldSeed: "season-1");
        var b = new FixedNpcPlacement("wandering_merchant", worldSeed: "season-2");
        a.GetCell(Stage1TestScene.Origin, out long r, out long c);
        Assert.Equal(a.NominalSite(r, c).Latitude, a2.NominalSite(r, c).Latitude, 12);
        Assert.NotEqual(a.NominalSite(r, c).Latitude, b.NominalSite(r, c).Latitude);
        Assert.Equal(FixedNpcPlacement.DefaultWorldSeed, new FixedNpcPlacement("x", worldSeed: null).WorldSeed);
    }

    [Fact]
    public void NearestSite_IsWithinTwoCellsOfThePlayer()
    {
        var p = new FixedNpcPlacement("wandering_merchant", 500);
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, Policy(OpenPark()), out var s));
        Assert.True(s.DistanceMeters < 2 * 500 * Math.Sqrt(2), s.DistanceMeters.ToString());
    }

    [Fact]
    public void WithoutPolicy_NoNpc_DefaultDeny()
    {
        var p = new FixedNpcPlacement("wandering_merchant");
        Assert.False(p.TryResolveNearest(Stage1TestScene.Origin, null, out _));
        p.AddLocalOverride(GeoMath.OffsetMeters(Stage1TestScene.Origin, 5, 5));
        Assert.False(p.TryResolveNearest(Stage1TestScene.Origin, null, out _)); // overrides are filtered too
    }

    [Fact]
    public void WithPolicy_SiteIsSafe_OrCellIsSkipped()
    {
        var scene = OpenPark(
            Stage1TestScene.F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Primary; f.Class = "primary"; }, line: new double[] { -Big, 0, Big, 0 }));
        var policy = Policy(scene);
        var p = new FixedNpcPlacement("wandering_merchant", 200);
        Assert.True(p.TryResolveNearest(scene.At(0, 0), policy, out var s));
        Assert.True(policy.EvaluateSite(s.Coordinate).Allowed);

        var nowhere = new EncounterSitePolicy(new Stage1TestScene(new List<ProjectedFeature>()));
        Assert.False(p.TryResolveNearest(scene.At(0, 0), nowhere, out _)); // no data → no NPC (default-deny)
    }

    public static IEnumerable<object[]> Hazards() => new[]
    {
        new object[] { "building" },
        new object[] { "water" },
        new object[] { "rail" },
        new object[] { "road" },
    };

    [Theory]
    [MemberData(nameof(Hazards))]
    public void NominalSiteOnHazard_IsMovedToSafeSpot_OrSkipped(string hazard)
    {
        var probe = new Stage1TestScene();
        var npc = new FixedNpcPlacement("wandering_merchant");
        npc.GetCell(Stage1TestScene.Origin, out long r, out long c);
        probe.ToLocal(npc.NominalSite(r, c), out double e, out double n);

        ProjectedFeature f = hazard switch
        {
            "building" => Stage1TestScene.F(MapFeatureKind.Building, polygon: Stage1TestScene.Box(e - 10, n - 10, e + 10, n + 10)),
            "water" => Stage1TestScene.F(MapFeatureKind.Water, x => x.Class = "lake", polygon: Stage1TestScene.Box(e - 15, n - 15, e + 15, n + 15)),
            "rail" => Stage1TestScene.F(MapFeatureKind.Rail, x => x.Class = "rail", line: new[] { e, n - 1000, e, n + 1000 }),
            _ => Stage1TestScene.F(MapFeatureKind.Road, x => { x.RoadClass = RoadClass.Primary; x.Class = "primary"; }, line: new[] { e - 1000, n, e + 1000, n }),
        };
        var scene = OpenPark(f);
        var policy = Policy(scene);
        Assert.False(policy.EvaluateSite(npc.NominalSite(r, c)).Allowed);

        Assert.True(npc.TryResolveNearest(npc.NominalSite(r, c), policy, out var s));
        Assert.True(policy.EvaluateSite(s.Coordinate).Allowed, hazard);
        Assert.True(GeoMath.DistanceMeters(s.Coordinate, npc.NominalSite(r, c)) > 1, hazard);
    }

    [Fact]
    public void AllWater_NoNpcAnywhere()
    {
        var scene = new Stage1TestScene(new List<ProjectedFeature>
        {
            Stage1TestScene.F(MapFeatureKind.Water, f => f.Class = "lake", polygon: Stage1TestScene.Box(-Big, -Big, Big, Big)),
        }, coverageHalfSize: Big);
        Assert.False(new FixedNpcPlacement("wandering_merchant").TryResolveNearest(Stage1TestScene.Origin, Policy(scene), out _));
    }

    [Fact]
    public void LocalOverride_WinsWhenNearer_AndSafe()
    {
        var policy = Policy(OpenPark());
        var p = new FixedNpcPlacement("wandering_merchant");
        var here = GeoMath.OffsetMeters(Stage1TestScene.Origin, 5, 5);
        p.AddLocalOverride(here);
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, policy, out var s));
        Assert.True(s.IsOverride);
        Assert.True(s.DistanceMeters < 8);
        p.ClearLocalOverrides();
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, policy, out var g));
        Assert.False(g.IsOverride);
    }

    [Fact]
    public void LocalOverride_OnBuilding_IsSkipped()
    {
        var scene = OpenPark(Stage1TestScene.F(MapFeatureKind.Building, polygon: Stage1TestScene.Box(-10, -10, 10, 10)));
        var p = new FixedNpcPlacement("wandering_merchant");
        p.AddLocalOverride(scene.At(0, 0));
        Assert.True(p.TryResolveNearest(scene.At(0, 0), Policy(scene), out var s));
        Assert.False(s.IsOverride);
    }

    [Fact]
    public void Repo_ContainsNoHardCodedCoordinates_InNpcPlacement()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "client")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string src = File.ReadAllText(Path.Combine(root!, "client/Assets/MetinAR/Scripts/Map/Core/FixedNpcPlacement.cs"));
        Assert.DoesNotMatch(@"new\s+GeoCoordinate\(\s*-?\d", src);
        Assert.DoesNotMatch(@"\b\d{1,3}\.\d{4,}\b", src);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RequiresId(string id)
    {
        Assert.Throws<ArgumentException>(() => new FixedNpcPlacement(id));
    }

    [Fact]
    public void RejectsTinyCells()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedNpcPlacement("x", 10));
    }
}
