using System;
using MetinAR.ArEncounter;
using Xunit;

// #31 decision 18:58 + #97: distance-only attack reach (kept); the „Podejdź” prompt is gone (#97: the mob walks, the player
// does not); v0 spawn range 1.10–3.0 m (prototype, #97 d_min).
public class ArApproachAndSpawnRangeTests
{
    [Fact]
    public void PrototypeSpawnRangeIsOnePointOneToThree()
    {
        var s = ArPlacementSettings.CreatePrototypeDefaults();
        Assert.Equal(1.1f, s.minSpawnDistanceMeters);
        Assert.Equal(ArEncounterFraming.MinPlacementDistanceMeters, new ArNoWalkSettings().minDistanceMeters);
        Assert.Equal(3.0f, s.maxSpawnDistanceMeters);
    }

    [Theory]
    [InlineData(0.8, ArSpawnDistance.TooClose)]
    [InlineData(1.08, ArSpawnDistance.TooClose)]
    [InlineData(1.095, ArSpawnDistance.InRange)]  // 1 cm tolerance
    [InlineData(1.1, ArSpawnDistance.InRange)]
    [InlineData(1.5, ArSpawnDistance.InRange)]
    [InlineData(2.4, ArSpawnDistance.InRange)]
    [InlineData(3.0, ArSpawnDistance.InRange)]
    [InlineData(3.005, ArSpawnDistance.InRange)]
    [InlineData(3.02, ArSpawnDistance.TooFar)]
    [InlineData(3.82, ArSpawnDistance.TooFar)]   // A31 v0 session placement
    public void ClassifiesSpawnDistance(double d, ArSpawnDistance expected)
    {
        var s = ArPlacementSettings.CreatePrototypeDefaults();
        // Distance along a diagonal to exercise the horizontal (x/z) metric.
        double k = d / Math.Sqrt(2);
        Assert.Equal(expected, ArEncounterFraming.ClassifySpawnDistance(1, 2, 1 + k, 2 - k, s.minSpawnDistanceMeters, s.maxSpawnDistanceMeters));
    }

    [Fact]
    public void ClassifyRejectsNonFinite()
    {
        Assert.Equal(ArSpawnDistance.Invalid, ArEncounterFraming.ClassifySpawnDistance(0, 0, double.NaN, 0, 1.5, 3.0));
        Assert.Equal(ArSpawnDistance.Invalid, ArEncounterFraming.ClassifySpawnDistance(0, 0, double.PositiveInfinity, 0, 1.5, 3.0));
    }

    [Theory]
    [InlineData(-0.1f, 3f)]
    [InlineData(3f, 3f)]
    [InlineData(3f, 1.5f)]
    [InlineData(1.1f, float.PositiveInfinity)]
    [InlineData(float.NaN, 3f)]
    public void StateMachineRejectsInvalidSpawnRange(float min, float max)
    {
        var s = ArPlacementSettings.CreatePrototypeDefaults();
        s.minSpawnDistanceMeters = min;
        s.maxSpawnDistanceMeters = max;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArEncounterStateMachine(s));
    }

    // ---- #31 18:58 correction: attack reach is DISTANCE ONLY (aim-independent) ----

    private static readonly FppPoint[] Blade =
    {
        new FppPoint(0.25, -0.08, 0.48), new FppPoint(0.20, 0.55, 0.80), new FppPoint(-0.15, -0.22, 0.50), new FppPoint(-0.40, -0.30, 1.05),
    };

    [Fact]
    public void BladeReachIsLongestBladePointAndIgnoresNonFinite()
    {
        var pts = new System.Collections.Generic.List<FppPoint>(Blade) { new FppPoint(double.NaN, 0, 9) };
        double expected = Math.Sqrt(0.40 * 0.40 + 0.30 * 0.30 + 1.05 * 1.05);
        Assert.Equal(expected, ArApproachPrompt.BladeReach(pts), 9);
        Assert.Equal(0.0, ArApproachPrompt.BladeReach(null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(180, 30)]   // aiming straight away from the mob, pitched down
    [InlineData(90, -20)]
    [InlineData(-135, 60)]
    public void BladeReachDoesNotDependOnAim(double yaw, double pitch)
    {
        var aimed = new FppPoint[Blade.Length];
        for (int i = 0; i < Blade.Length; i++) aimed[i] = FppViewFraming.Rotate(new FppPoint(pitch, yaw, 0), Blade[i]);
        Assert.Equal(ArApproachPrompt.BladeReach(Blade), ArApproachPrompt.BladeReach(aimed), 9);
    }

    private static double ToFootprint(double x, double z) => ArApproachPrompt.DistanceToFootprint(
        x, z, FppHitGeometry.BodyMin.X, FppHitGeometry.ReachColumnMax.X, FppHitGeometry.BodyMin.Z, FppHitGeometry.ReachColumnMax.Z);

    [Fact]
    public void FootprintDistanceUsesTheExistingHitFootprint()
    {
        Assert.Equal(0.0, ToFootprint(0, 0));                                 // inside
        Assert.Equal(1.0 - FppHitGeometry.ReachColumnMax.Z, ToFootprint(0, 1.0), 9);  // in front (mob faces +Z)
        Assert.Equal(1.0 + FppHitGeometry.BodyMin.Z, ToFootprint(0, -1.0), 9);        // behind
        Assert.Equal(0.5, ToFootprint(FppHitGeometry.ReachColumnMax.X + 0.3, FppHitGeometry.ReachColumnMax.Z + 0.4), 9);
        Assert.True(double.IsNaN(ToFootprint(double.NaN, 0)));
    }

    [Fact]
    public void UpCloseInReach_FarAwayOutOfReach()
    {
        double reach = ArApproachPrompt.BladeReach(Blade);
        // Same camera position whatever the aim: the decision has no aim input at all.
        Assert.False(ArApproachPrompt.IsOutOfReach(ToFootprint(0, 1.0), reach));
        Assert.True(ArApproachPrompt.IsOutOfReach(ToFootprint(0, 3.82), reach));
    }

    [Fact]
    public void OutOfReachBoundaryAndInvalid()
    {
        Assert.False(ArApproachPrompt.IsOutOfReach(1.2, 1.2));
        Assert.True(ArApproachPrompt.IsOutOfReach(1.2001, 1.2));
        Assert.False(ArApproachPrompt.IsOutOfReach(double.NaN, 1.2));
        Assert.False(ArApproachPrompt.IsOutOfReach(2.0, double.NaN));
    }
}
