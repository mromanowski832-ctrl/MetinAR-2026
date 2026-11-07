using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class MovementSpeedEstimatorTests
{
    [Fact]
    public void UnknownUntilWindowSpansTwoSeconds()
    {
        var m = new MovementSpeedEstimator();
        Assert.True(double.IsNaN(m.OnFix(0, 0, 4, 0)));
        Assert.True(double.IsNaN(m.OnFix(1, 0, 4, 1)));
        Assert.False(double.IsNaN(m.OnFix(2, 0, 4, 2)));
    }

    [Fact]
    public void StandingStill_WithJitter_IsBelowEntryLimit()
    {
        var m = new MovementSpeedEstimator();
        double limit = new EncounterEligibilitySettings().MaxEntrySpeedMetersPerSecond;
        double[] jx = { 0, 2, -1.5, 1, -2, 1.8, -0.5, 2, -2, 0.5, 1.5, -1 };
        double[] jy = { 0, -1, 2, -2, 1, 0.5, -1.8, 1.2, -0.3, 2, -1.5, 0.8 };
        for (int t = 0; t < jx.Length; t++)
        {
            double v = m.OnFix(jx[t], jy[t], 5, t);
            Assert.True(double.IsNaN(v) || v <= limit, $"t={t} v={v}");
        }
    }

    [Theory]
    [InlineData(1.4, 4)]
    [InlineData(1.4, 5)]
    [InlineData(2.5, 8)]
    public void Walking_IsAboveEntryLimit(double speed, double acc)
    {
        var m = new MovementSpeedEstimator();
        double limit = new EncounterEligibilitySettings().MaxEntrySpeedMetersPerSecond;
        double v = double.NaN;
        for (int t = 0; t <= 6; t++)
        {
            v = m.OnFix(speed * t, 0, acc, t);
        }

        Assert.True(v > limit, v.ToString());
    }

    [Fact]
    public void DuplicateTimestamp_Ignored_BackwardsTime_Resets()
    {
        var m = new MovementSpeedEstimator();
        m.OnFix(0, 0, 3, 0);
        m.OnFix(4, 0, 3, 2);
        double v = m.SpeedMetersPerSecond;
        Assert.Equal(v, m.OnFix(100, 0, 3, 2));
        Assert.True(double.IsNaN(m.OnFix(0, 0, 3, 1)));
    }
}
