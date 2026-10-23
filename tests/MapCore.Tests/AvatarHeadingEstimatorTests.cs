using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class AvatarHeadingEstimatorTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10, 0, 90)]
    [InlineData(0, -10, 180)]
    [InlineData(-10, 0, 270)]
    [InlineData(10, 10, 45)]
    public void WalkingCourse_GivesCompassHeading(double de, double dn, double expected)
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 3, 0);
        Assert.True(h.OnFix(de, dn, 3, 8)); // ~1.25-1.8 m/s
        Assert.Equal(expected, h.TargetDegrees, 6);
        Assert.Equal(HeadingSource.Course, h.Source);
        Assert.Equal(expected, h.VisualDegrees, 6); // first heading snaps
    }

    [Fact]
    public void Jitter_DoesNotSpinTheAvatar()
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 5, 0);
        Assert.True(h.OnFix(0, 10, 5, 7));
        var rnd = new Random(7);
        for (int i = 0; i < 30; i++)
        {
            // standing still at (0,10) with ±3 m noise: below max(4, 0.8*5) threshold
            Assert.False(h.OnFix(rnd.NextDouble() * 4 - 2, 10 + rnd.NextDouble() * 4 - 2, 5, 8 + i));
        }

        Assert.Equal(0, h.TargetDegrees, 6);
    }

    [Fact]
    public void LargeAccuracy_RaisesMovementThreshold()
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 20, 0);
        Assert.False(h.OnFix(10, 0, 20, 10)); // 10 m < 0.8*20 = 16 m
        Assert.True(h.OnFix(18, 0, 20, 20));  // 18 m from anchor
        Assert.Equal(90, h.TargetDegrees, 6);
    }

    [Fact]
    public void GpsJump_IsNotACourse_AndRestartsAnchor()
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 3, 0);
        Assert.True(h.OnFix(0, 10, 3, 8));            // heading north
        Assert.False(h.OnFix(100, 10, 3, 9));         // 100 m in 1 s: jump
        Assert.Equal(0, h.TargetDegrees, 6);
        Assert.True(h.OnFix(100, 0, 3, 17));          // walking south from the new anchor
        Assert.Equal(180, h.TargetDegrees, 6);
    }

    [Fact]
    public void Compass_UsedOnlyWhenStandingStill_ElseHeld()
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 3, 0);
        h.OnFix(10, 0, 3, 8);             // course 90 at t=8
        h.OnCompass(200, 10);              // course still fresh (<= 4 s): ignored
        Assert.Equal(90, h.TargetDegrees, 6);
        Assert.Equal(HeadingSource.Course, h.Source);
        h.OnCompass(200, 13);              // standing still: compass wins
        Assert.Equal(200, h.TargetDegrees, 6);
        Assert.Equal(HeadingSource.Compass, h.Source);
        h.OnCompass(null, 14);             // compass unavailable: hold
        Assert.Equal(200, h.TargetDegrees, 6);
        Assert.Equal(HeadingSource.Held, h.Source);
    }

    [Fact]
    public void Visual_TurnsSmoothlyAlongShortestArc_AcrossNorth()
    {
        var h = new AvatarHeadingEstimator();
        h.OnFix(0, 0, 3, 0);
        h.OnFix(-1.74, 9.85, 3, 8);       // ~350°
        Assert.Equal(350, h.VisualDegrees, 0);
        h.OnFix(-1.74 + 1.74, 9.85 + 9.85, 3, 16); // ~10°
        Assert.Equal(10, h.TargetDegrees, 0);
        double maxStep = 0;
        for (int i = 0; i < 60; i++)
        {
            maxStep = Math.Max(maxStep, h.Step(1 / 30.0));
            double v = h.VisualDegrees;
            Assert.True(v >= 349 || v <= 10.5, "must turn through north, got " + v);
        }

        Assert.Equal(10, h.VisualDegrees, 0);
        Assert.True(maxStep <= 180 / 30.0 + 1e-9);
    }

    [Theory]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, -20)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 90, 0)]
    public void ShortestDelta(double from, double to, double expected)
    {
        Assert.Equal(expected, AvatarHeadingEstimator.ShortestDelta(from, to), 9);
    }
}
