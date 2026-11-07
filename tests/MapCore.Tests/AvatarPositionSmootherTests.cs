using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class AvatarPositionSmootherTests
{
    private const double Frame = 1.0 / 30.0;

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>Runs frames for <paramref name="seconds"/>; returns the largest single-frame visual step.</summary>
    private static double RunFrames(AvatarPositionSmoother s, double seconds, double dt = Frame)
    {
        double maxStep = 0;
        for (double t = 0; t < seconds - 1e-9; t += dt)
        {
            maxStep = Math.Max(maxStep, s.Step(dt));
        }

        return maxStep;
    }

    [Fact]
    public void FirstFix_Snaps_AndDuplicateIsNoOp()
    {
        var s = new AvatarPositionSmoother();
        Assert.Equal(AvatarFixResult.First, s.OnFix(10, 20, 5, 100));
        Assert.Equal(10, s.VisualEast);
        Assert.Equal(20, s.VisualNorth);
        Assert.Equal(AvatarFixResult.Duplicate, s.OnFix(15, 25, 5, 100));
        Assert.Equal(10, s.TargetEast);
    }

    [Fact]
    public void StaleTimestamp_IsIgnored()
    {
        var s = new AvatarPositionSmoother();
        s.OnFix(0, 0, 5, 100);
        s.OnFix(5, 0, 5, 101);
        Assert.Equal(AvatarFixResult.Stale, s.OnFix(-30, 0, 5, 99));
        Assert.Equal(5, s.TargetEast);
    }

    [Theory]
    [InlineData(0, 2.0)]
    [InlineData(10, 3.0)]
    [InlineData(100, 6.0)]
    [InlineData(double.NaN, 2.0)]
    public void JitterThreshold_ScalesWithAccuracy_AndIsBounded(double acc, double expected)
    {
        Assert.Equal(expected, new AvatarPositionSmoother().JitterThreshold(acc), 9);
    }

    [Fact]
    public void StandingStill_WithGpsJitter_VisualBarelyMoves()
    {
        var s = new AvatarPositionSmoother();
        var rng = new Random(42);
        s.OnFix(0, 0, 5, 0);
        double worst = 0;
        for (int i = 1; i <= 60; i++)
        {
            double r = rng.NextDouble() * 1.8, th = rng.NextDouble() * 2 * Math.PI;
            double e = r * Math.Cos(th), n = r * Math.Sin(th);
            s.OnFix(e, n, 5, i);
            RunFrames(s, 1.0);
            worst = Math.Max(worst, Dist(s.VisualEast, s.VisualNorth, 0, 0));
        }

        Assert.True(worst < 0.01, "visual drifted " + worst + " m while standing still");
    }

    [Fact]
    public void StandingStill_WithHeavyJitter_DriftIsBounded_AndNeverTeleports()
    {
        var s = new AvatarPositionSmoother();
        var rng = new Random(7);
        s.OnFix(0, 0, 12, 0);
        double worst = 0, maxStep = 0;
        for (int i = 1; i <= 120; i++)
        {
            double r = rng.NextDouble() * 5.0, th = rng.NextDouble() * 2 * Math.PI;
            s.OnFix(r * Math.Cos(th), r * Math.Sin(th), 12, i);
            maxStep = Math.Max(maxStep, RunFrames(s, 1.0));
            worst = Math.Max(worst, Dist(s.VisualEast, s.VisualNorth, 0, 0));
        }

        Assert.True(maxStep <= 6.0 * Frame + 1e-9, "per-frame step " + maxStep);
        Assert.True(worst <= 5.0 + 3.6 + 1e-6, "drift " + worst);
    }

    [Fact]
    public void Walking_1Hz_Fixes_MovesSmoothly_WithBoundedLag()
    {
        var s = new AvatarPositionSmoother();
        double maxStep = 0, maxLag = 0;
        s.OnFix(0, 0, 5, 0);
        for (int i = 1; i <= 60; i++)
        {
            Assert.NotEqual(AvatarFixResult.HeldOutlier, s.OnFix(0, 1.4 * i, 5, i));
            for (int f = 0; f < 30; f++)
            {
                maxStep = Math.Max(maxStep, s.Step(Frame));
                maxLag = Math.Max(maxLag, Dist(s.VisualEast, s.VisualNorth, 0, 1.4 * i));
            }
        }

        // Raw positions would jump 1.4–2.8 m per fix; the visual moves at most MaxVisualSpeed * dt per frame.
        Assert.True(maxStep <= 6.0 * Frame + 1e-9, "per-frame step " + maxStep);
        Assert.True(maxLag < 6.0, "lag " + maxLag);
        Assert.True(s.VisualNorth > 1.4 * 60 - 6.0, "visual fell behind: " + s.VisualNorth);
    }

    [Fact]
    public void SingleOutlier_IsHeld_AndDiscardedByNextNormalFix()
    {
        var s = new AvatarPositionSmoother();
        s.OnFix(0, 0, 5, 0);
        Assert.Equal(AvatarFixResult.HeldOutlier, s.OnFix(40, 0, 5, 1));
        RunFrames(s, 1.0);
        Assert.Equal(0, s.VisualEast, 9);
        Assert.Equal(AvatarFixResult.IgnoredJitter, s.OnFix(0.5, 0.5, 5, 2));
        Assert.False(s.HasPendingOutlier);
        RunFrames(s, 3.0);
        Assert.True(Dist(s.VisualEast, s.VisualNorth, 0, 0) < 0.01);
    }

    [Fact]
    public void FastJump_ConfirmedByNextFix_IsAccepted_AndGlidesWithSpeedCap()
    {
        var s = new AvatarPositionSmoother();
        s.OnFix(0, 0, 5, 0);
        Assert.Equal(AvatarFixResult.HeldOutlier, s.OnFix(30, 0, 5, 1));
        Assert.Equal(AvatarFixResult.ConfirmedJump, s.OnFix(31, 0, 5, 2));
        double maxStep = RunFrames(s, 1.0);
        Assert.True(maxStep <= 6.0 * Frame + 1e-9);
        Assert.InRange(s.VisualEast, 5.0, 6.0 + 1e-6); // ~1 s at the 6 m/s cap
        RunFrames(s, 10.0);
        Assert.True(s.LagMeters < 0.05);
    }

    [Fact]
    public void Relock_AfterLongGap_FarAway_Snaps()
    {
        var s = new AvatarPositionSmoother();
        s.OnFix(0, 0, 5, 0);
        Assert.Equal(AvatarFixResult.Snapped, s.OnFix(200, 0, 8, 20));
        Assert.Equal(200, s.VisualEast);
    }

    [Fact]
    public void LoneTeleport_WithNoNewerFix_IsAcceptedAfterTimeout()
    {
        // Mock/demo move: one far fix and then nothing (the mock only emits on change).
        var s = new AvatarPositionSmoother();
        s.OnFix(0, 0, 5, 0);
        Assert.Equal(AvatarFixResult.HeldOutlier, s.OnFix(900, 0, 5, 1));
        RunFrames(s, 1.0);
        Assert.Equal(0, s.VisualEast, 9);
        RunFrames(s, 1.5);
        Assert.False(s.HasPendingOutlier);
        Assert.Equal(1, s.PendingTimeouts);
        Assert.Equal(900, s.VisualEast, 6);
    }

    [Fact]
    public void Step_IsFrameRateIndependent_WhenNotSpeedCapped()
    {
        var a = new AvatarPositionSmoother();
        var b = new AvatarPositionSmoother();
        foreach (var s in new[] { a, b })
        {
            s.OnFix(0, 0, 5, 0);
            s.OnFix(3, 0, 5, 1);
        }

        RunFrames(a, 0.5, 1.0 / 30.0);
        RunFrames(b, 0.5, 1.0 / 60.0);
        Assert.True(Math.Abs(a.VisualEast - b.VisualEast) < 0.05, a.VisualEast + " vs " + b.VisualEast);
    }

    [Fact]
    public void NaNInput_IsRejected()
    {
        var s = new AvatarPositionSmoother();
        Assert.Equal(AvatarFixResult.Stale, s.OnFix(double.NaN, 0, 5, 0));
        Assert.False(s.HasPosition);
    }
}
