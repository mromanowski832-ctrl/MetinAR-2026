using MetinAR.ArEncounter;
using Xunit;

namespace GeoCore.Tests;

/// <summary>#31 v0 acceptance: diagnostics for "the spider moves together with the phone".</summary>
public class ArAnchorProbeTests
{
    // Camera at the origin facing +Z (yaw 0), spider 2 m ahead on the floor.
    private static ArAnchorProbe Placed()
    {
        var p = new ArAnchorProbe();
        p.SetBaseline(0, 0, 0, 0, 0, 2);
        return p;
    }

    [Fact]
    public void RotatingInPlaceKeepsTheSpider()
    {
        ArAnchorSample s = Placed().Sample(0, 0, 90, 0, 0, 2);
        Assert.Equal("STAYS", s.Verdict);
        Assert.Equal(90, s.Yaw, 3);
        Assert.Equal(0, s.CameraMove, 3);
    }

    [Fact]
    public void SidestepIsLateralAndSpiderStays()
    {
        ArAnchorSample s = Placed().Sample(0.5, 0, 0, 0, 0, 2);
        Assert.Equal("STAYS", s.Verdict);
        Assert.Equal(0.5, s.Sidestep, 3);
        Assert.Equal(0, s.Closer, 3);
    }

    [Fact]
    public void WalkingCloserAndFartherIsRadial()
    {
        ArAnchorProbe p = Placed();
        ArAnchorSample closer = p.Sample(0, 1, 0, 0, 0, 2);
        Assert.Equal(1, closer.Closer, 3);
        Assert.Equal(1, closer.Distance, 3);
        ArAnchorSample farther = p.Sample(0, -1, 0, 0, 0, 2);
        Assert.Equal(-1, farther.Closer, 3);
        Assert.Equal(3, farther.Distance, 3);
        Assert.Equal("STAYS", farther.Verdict);
        Assert.Equal(0, p.FollowsCameraSamples);
    }

    [Fact]
    public void SpiderMovingWithTheCameraIsFlagged()
    {
        ArAnchorProbe p = Placed();
        // Owner report: phone moved 0.6 m forward and the spider moved forward with it.
        ArAnchorSample s = p.Sample(0, 0.6, 0, 0, 0, 2.6);
        Assert.True(s.FollowsCamera);
        Assert.Equal("FOLLOWS_CAMERA", s.Verdict);
        Assert.Equal(2, s.Distance, 3);
        Assert.Equal(1, p.FollowsCameraSamples);
        Assert.Equal(0.6, p.MaxDriftMeters, 3);
    }

    [Fact]
    public void DriftWithoutCameraMoveIsDriftNotFollow()
    {
        ArAnchorSample s = Placed().Sample(0, 0, 0, 0.3, 0, 2);
        Assert.Equal("DRIFT", s.Verdict);
        Assert.False(s.FollowsCamera);
    }

    [Fact]
    public void TrackingRecoveryJumpBackIsNotAFollow()
    {
        ArAnchorProbe p = Placed();
        // After tracking recovery the camera pose jumps, the spider stays put.
        ArAnchorSample s = p.Sample(0.8, -0.4, 35, 0, 0, 2);
        Assert.Equal("STAYS", s.Verdict);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(360, 0)]
    [InlineData(180, 180)]
    public void YawWraps(double input, double expected)
    {
        Assert.Equal(expected, ArAnchorProbe.WrapDegrees(input), 6);
    }

    [Fact]
    public void LogLineIsInvariantCulture()
    {
        string line = Placed().Sample(0, 0.5, 10, 0, 0, 2).ToString();
        Assert.Contains("verdict=STAYS", line);
        Assert.Contains("closer=0.50", line);
    }
}
