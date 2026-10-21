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
}
