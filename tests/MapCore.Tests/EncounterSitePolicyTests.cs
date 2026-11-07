using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class EncounterSitePolicyTests
{
    private static (Stage1TestScene scene, EncounterSitePolicy policy) Make(EncounterSitePolicySettings s = null)
    {
        var scene = new Stage1TestScene();
        return (scene, new EncounterSitePolicy(scene, s));
    }

    [Fact]
    public void OpenPark_FarFromEverything_IsAllowed()
    {
        var (scene, policy) = Make();
        var d = policy.EvaluateSite(scene.At(0, -150));
        Assert.True(d.Allowed, d.ToString());
        Assert.Equal(SafetyReason.None, d.Reason);
    }

    [Theory]
    [InlineData(0, 5, SafetyReason.Road)]        // on the primary road
    [InlineData(0, 18, SafetyReason.Road)]       // 18 m: outside the 15 m prototype buffer but inside +5 m Stage-I clearance
    [InlineData(395, -100, SafetyReason.Rail)]   // next to rail
    [InlineData(250, 150, SafetyReason.Water)]   // in the river
    [InlineData(250, 95, SafetyReason.Water)]    // shore within buffer
    [InlineData(-90, 60, SafetyReason.Building)] // inside a building
    [InlineData(-350, -350, SafetyReason.Private)] // industrial landuse
    public void ExcludedZones_AreRejected(double e, double n, SafetyReason expected)
    {
        var (scene, policy) = Make();
        var d = policy.EvaluateSite(scene.At(e, n));
        Assert.False(d.Allowed);
        Assert.Equal(expected, d.Reason);
    }

    [Fact]
    public void NoData_OutsideCoverage_IsRejected_DefaultDeny()
    {
        var (scene, policy) = Make();
        var d = policy.EvaluateSite(scene.At(0, 700));
        Assert.False(d.Allowed);
        Assert.Equal(SafetyReason.NoData, d.Reason);
    }

    [Fact]
    public void UnclassifiedSurface_IsRejected_DefaultDeny()
    {
        var (scene, policy) = Make();
        var d = policy.EvaluateSite(scene.At(0, 400)); // covered, but no landcover/park/landuse polygon
        Assert.False(d.Allowed);
        Assert.Equal(SafetyReason.UnclassifiedSurface, d.Reason);
    }

    [Fact]
    public void EmptyMap_NeverSpawns()
    {
        var scene = new Stage1TestScene(new List<ProjectedFeature>());
        var policy = new EncounterSitePolicy(scene);
        Assert.False(policy.EvaluateSite(scene.At(0, 0)).Allowed);
        Assert.False(policy.TryPlace(scene.At(0, 0), 40, out _, out var why));
        Assert.Equal(SafetyReason.UnclassifiedSurface, why.Reason);
    }

    [Fact]
    public void Footprint_RejectsSiteWhoseCentreIsClearButEdgeTouchesBuilding()
    {
        // Centre 9 m from the building edge: centre clearance 2 + 5 = 7 m passes, footprint radius 8 m reaches within 2 m buffer.
        var (scene, policy) = Make();
        var d = policy.EvaluateSite(scene.At(-90, 79));
        Assert.False(d.Allowed);
        Assert.Equal(SafetyReason.Building, d.Reason);
        Assert.StartsWith("footprint", d.Detail);

        var noFootprint = new EncounterSitePolicy(scene, new EncounterSitePolicySettings { FootprintRadiusMeters = 0 });
        Assert.True(noFootprint.EvaluateSite(scene.At(-90, 79)).Allowed);
    }

    [Fact]
    public void TryPlace_MovesSiteOffTheRoad_ToASafeSpot()
    {
        var (scene, policy) = Make();
        Assert.True(policy.TryPlace(scene.At(0, 3), 60, out GeoCoordinate site, out var first));
        Assert.False(first.Allowed);
        Assert.Equal(SafetyReason.Road, first.Reason);
        Assert.True(policy.EvaluateSite(site).Allowed);
        scene.ToLocal(site, out _, out double n);
        Assert.True(Math.Abs(n) >= 15 + 5 - 0.01, "site must clear prototype buffer + Stage-I clearance, got " + n);
    }

    [Fact]
    public void TryPlace_FailsInsideLargeWater_NoFallbackSpawn()
    {
        var scene = new Stage1TestScene(new List<ProjectedFeature>
        {
            Stage1TestScene.F(MapFeatureKind.Water, f => f.Class = "lake", polygon: Stage1TestScene.Box(-500, -500, 500, 500)),
        });
        var policy = new EncounterSitePolicy(scene);
        Assert.False(policy.TryPlace(scene.At(0, 0), 60, out GeoCoordinate site, out var why));
        Assert.Equal(SafetyReason.Water, why.Reason);
        Assert.Equal(scene.At(0, 0).Latitude, site.Latitude, 9);
    }

    [Fact]
    public void TryPlace_IsDeterministic()
    {
        var (scene, policy) = Make();
        Assert.True(policy.TryPlace(scene.At(-90, 60), 60, out var a, out _));
        Assert.True(policy.TryPlace(scene.At(-90, 60), 60, out var b, out _));
        Assert.Equal(a.Latitude, b.Latitude, 12);
        Assert.Equal(a.Longitude, b.Longitude, 12);
    }

    [Fact]
    public void PlayerSpot_OnRoadRailWater_RefusesFight_ButUnknownSurfaceIsTolerated()
    {
        var (scene, policy) = Make();
        Assert.False(policy.IsPlayerSpotAcceptable(scene.At(0, 2), out var r1));
        Assert.Equal(SafetyReason.Road, r1);
        Assert.False(policy.IsPlayerSpotAcceptable(scene.At(400, -100), out var r2));
        Assert.Equal(SafetyReason.Rail, r2);
        Assert.False(policy.IsPlayerSpotAcceptable(scene.At(250, 150), out var r3));
        Assert.Equal(SafetyReason.Water, r3);
        Assert.True(policy.IsPlayerSpotAcceptable(scene.At(0, 400), out _));   // unclassified
        Assert.True(policy.IsPlayerSpotAcceptable(scene.At(0, -150), out _));  // park

        var off = new EncounterSitePolicy(scene, new EncounterSitePolicySettings { CheckPlayerSpot = false });
        Assert.True(off.IsPlayerSpotAcceptable(scene.At(0, 2), out _));
    }

    [Fact]
    public void PrototypeSpawnSafetyConfig_IsNotModified_D4()
    {
        var cfg = new SpawnSafetyConfig();
        Assert.Equal(15, cfg.PrimaryBuffer);
        Assert.Equal(15, cfg.RailBuffer);
        Assert.Equal(5, cfg.WaterBuffer);
        Assert.Equal(2, cfg.BuildingBuffer);
    }
}
