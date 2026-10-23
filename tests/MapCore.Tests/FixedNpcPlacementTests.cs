using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class FixedNpcPlacementTests
{
    [Fact]
    public void SameNpcSameCell_SameSite_AcrossInstances()
    {
        var a = new FixedNpcPlacement("wandering_merchant");
        var b = new FixedNpcPlacement("wandering_merchant");
        var p = Stage1TestScene.Origin;
        Assert.True(a.TryResolveNearest(p, null, out var sa));
        Assert.True(b.TryResolveNearest(GeoMath.OffsetMeters(p, 30, -20), null, out var sb)); // player moved a bit
        Assert.Equal(sa.Coordinate.Latitude, sb.Coordinate.Latitude, 12);
        Assert.Equal(sa.Coordinate.Longitude, sb.Coordinate.Longitude, 12);
        Assert.False(sa.IsOverride);
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
    public void NearestSite_IsWithinTwoCellsOfThePlayer()
    {
        var p = new FixedNpcPlacement("wandering_merchant", 500);
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, null, out var s));
        Assert.True(s.DistanceMeters < 2 * 500 * Math.Sqrt(2), s.DistanceMeters.ToString());
    }

    [Fact]
    public void WithPolicy_SiteIsSafe_OrCellIsSkipped()
    {
        var scene = new Stage1TestScene(coverageHalfSize: 5000, features: new List<ProjectedFeature>
        {
            Stage1TestScene.F(MapFeatureKind.Park, f => f.Class = "park", polygon: Stage1TestScene.Box(-5000, -5000, 5000, 5000)),
            Stage1TestScene.F(MapFeatureKind.Road, f => { f.RoadClass = RoadClass.Primary; f.Class = "primary"; }, line: new double[] { -5000, 0, 5000, 0 }),
        });
        var policy = new EncounterSitePolicy(scene);
        var p = new FixedNpcPlacement("wandering_merchant", 200);
        Assert.True(p.TryResolveNearest(scene.At(0, 0), policy, out var s));
        Assert.True(policy.EvaluateSite(s.Coordinate).Allowed);

        var nowhere = new EncounterSitePolicy(new Stage1TestScene(new List<ProjectedFeature>()));
        Assert.False(p.TryResolveNearest(scene.At(0, 0), nowhere, out _)); // no data → no NPC (default-deny)
    }

    [Fact]
    public void LocalOverride_WinsWhenNearer()
    {
        var p = new FixedNpcPlacement("wandering_merchant");
        var here = GeoMath.OffsetMeters(Stage1TestScene.Origin, 5, 5);
        p.AddLocalOverride(here);
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, null, out var s));
        Assert.True(s.IsOverride);
        Assert.True(s.DistanceMeters < 8);
        p.ClearLocalOverrides();
        Assert.True(p.TryResolveNearest(Stage1TestScene.Origin, null, out var g));
        Assert.False(g.IsOverride);
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
