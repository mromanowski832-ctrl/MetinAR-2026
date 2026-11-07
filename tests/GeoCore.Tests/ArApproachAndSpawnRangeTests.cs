using System;
using MetinAR.ArEncounter;
using Xunit;

// #31 decision 18:58: v0 mob stands still, „Podejdź – x,x m” out of reach only; v0 spawn range 1.5–3.0 m (prototype).
public class ArApproachAndSpawnRangeTests
{
    [Theory]
    [InlineData(2.4, "Podejdź – 2,4 m")]
    [InlineData(2.449, "Podejdź – 2,4 m")]
    [InlineData(2.45, "Podejdź – 2,5 m")]
    [InlineData(3.82, "Podejdź – 3,8 m")]
    [InlineData(1.0, "Podejdź – 1,0 m")]
    [InlineData(0.0, "Podejdź – 0,0 m")]
    public void FormatUsesPolishCommaAndOneDecimal(double meters, string expected)
    {
        Assert.Equal(expected, ArApproachPrompt.Format(meters));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    public void FormatRejectsInvalidDistance(double meters)
    {
        Assert.Equal(string.Empty, ArApproachPrompt.Format(meters));
    }

    [Theory]
    //          placed usable defeated inReach expected
    [InlineData(true,  true,  false, false, true)]   // out of reach → shown
    [InlineData(true,  true,  false, true,  false)]  // in reach → hidden
    [InlineData(true,  false, false, false, false)]  // tracking lost / relocating → „Szukam otoczenia…” instead
    [InlineData(true,  true,  true,  false, false)]  // defeated → nothing to walk up to
    [InlineData(false, true,  false, false, false)]  // not placed
    public void ShowsOnlyOutOfReachForUsableLiveMob(bool placed, bool usable, bool defeated, bool inReach, bool expected)
    {
        Assert.Equal(expected, ArApproachPrompt.ShouldShow(placed, usable, defeated, inReach, 2.4));
    }

    [Fact]
    public void HiddenForInvalidDistance()
    {
        Assert.False(ArApproachPrompt.ShouldShow(true, true, false, false, double.NaN));
    }

    [Fact]
    public void PrototypeSpawnRangeIsOnePointFiveToThree()
    {
        var s = ArPlacementSettings.CreatePrototypeDefaults();
        Assert.Equal(1.5f, s.minSpawnDistanceMeters);
        Assert.Equal(3.0f, s.maxSpawnDistanceMeters);
    }

    [Theory]
    [InlineData(0.8, ArSpawnDistance.TooClose)]
    [InlineData(1.48, ArSpawnDistance.TooClose)]
    [InlineData(1.495, ArSpawnDistance.InRange)]  // 1 cm tolerance
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
    [InlineData(1.5f, float.PositiveInfinity)]
    [InlineData(float.NaN, 3f)]
    public void StateMachineRejectsInvalidSpawnRange(float min, float max)
    {
        var s = ArPlacementSettings.CreatePrototypeDefaults();
        s.minSpawnDistanceMeters = min;
        s.maxSpawnDistanceMeters = max;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArEncounterStateMachine(s));
    }

    // ---- #31 18:58 correction: attack reach for the prompt is DISTANCE ONLY (aim-independent) ----

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
    public void UpCloseAimingAwayNoPrompt_FarAwayPrompt()
    {
        double reach = ArApproachPrompt.BladeReach(Blade);
        // Same camera position whatever the aim: the decision has no aim input at all.
        bool closeOut = ArApproachPrompt.IsOutOfReach(ToFootprint(0, 1.0), reach);
        Assert.False(closeOut);
        Assert.False(ArApproachPrompt.ShouldShow(true, true, false, !closeOut, 1.0));
        bool farOut = ArApproachPrompt.IsOutOfReach(ToFootprint(0, 3.82), reach);
        Assert.True(farOut);
        Assert.True(ArApproachPrompt.ShouldShow(true, true, false, !farOut, 3.82));
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
