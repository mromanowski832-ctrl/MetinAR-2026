using System;
using MetinAR.ArEncounter;
using Xunit;

// #31 MANAGER DECISION 18:50: hide + pause at once, ARSession reset after 15 s (prototype), mob anchored again after it.
public class ArTrackingRecoveryTests
{
    private const float Frame = 1f / 30f;

    private static ArTrackingRecovery Prototype() =>
        new ArTrackingRecovery(ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds);

    [Fact]
    public void PrototypeDefaultIsFifteenSeconds()
    {
        Assert.Equal(15f, ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds);
        Assert.Equal(15f, Prototype().ResetAfterSeconds);
    }

    [Fact]
    public void TrackingLossHidesAtOnceAndResetsOnlyAfterFifteenSeconds()
    {
        var r = Prototype();
        Assert.Equal(ArRecoveryAction.None, r.Tick(true, true, Frame));
        Assert.True(r.MobUsable);

        Assert.Equal(ArRecoveryAction.None, r.Tick(true, false, Frame));
        Assert.False(r.MobUsable); // first lost frame: hidden, combat paused

        int resets = 0;
        float t = Frame;
        while (t < 14.9f)
        {
            if (r.Tick(true, false, Frame) == ArRecoveryAction.ResetSession) resets++;
            t += Frame;
        }

        Assert.Equal(0, resets);
        Assert.False(r.MobUsable);
        while (t < 15.2f)
        {
            if (r.Tick(true, false, Frame) == ArRecoveryAction.ResetSession) resets++;
            t += Frame;
        }

        Assert.Equal(1, resets);
        Assert.Equal(1, r.ResetCount);
        Assert.True(r.AwaitingRelocation);
        Assert.InRange(r.LastResetLostSeconds, 15f, 15f + Frame + 1e-3f);
    }

    [Fact]
    public void ShortLossRecoversWithoutReset()
    {
        var r = Prototype();
        for (int i = 0; i < 30 * 10; i++) Assert.Equal(ArRecoveryAction.None, r.Tick(true, false, Frame));
        Assert.Equal(ArRecoveryAction.None, r.Tick(true, true, Frame));
        Assert.True(r.MobUsable);
        Assert.Equal(0, r.ResetCount);
        Assert.Equal(0f, r.LostSeconds);
        // A new loss starts counting from zero.
        for (int i = 0; i < 30 * 10; i++) Assert.Equal(ArRecoveryAction.None, r.Tick(true, false, Frame));
        Assert.Equal(0, r.ResetCount);
    }

    [Fact]
    public void AfterResetMobStaysHiddenUntilRelocated()
    {
        var r = new ArTrackingRecovery(15f);
        Assert.Equal(ArRecoveryAction.ResetSession, r.Tick(true, false, 16f));
        Assert.Equal(ArRecoveryAction.None, r.Tick(true, false, Frame));
        Assert.False(r.MobUsable);
        // Tracking back but no new ground yet: keep asking to relocate, still hidden.
        Assert.Equal(ArRecoveryAction.Relocate, r.Tick(true, true, Frame));
        Assert.Equal(ArRecoveryAction.Relocate, r.Tick(true, true, Frame));
        Assert.False(r.MobUsable);
        r.MarkRelocated();
        Assert.True(r.MobUsable);
        Assert.False(r.AwaitingRelocation);
        Assert.Equal(ArRecoveryAction.None, r.Tick(true, true, Frame));
        Assert.Equal(1, r.ResetCount);
    }

    [Fact]
    public void ResetRepeatsWhenTrackingNeverReturns()
    {
        var r = new ArTrackingRecovery(15f);
        int resets = 0;
        for (int i = 0; i < 30 * 46; i++)
            if (r.Tick(true, false, Frame) == ArRecoveryAction.ResetSession) resets++;
        Assert.Equal(3, resets); // at about 15, 30 and 45 s
        Assert.InRange(r.EpisodeLostSeconds, 45.9f, 46.1f);
    }

    [Fact]
    public void NotPlacedNeverResets()
    {
        var r = new ArTrackingRecovery(15f);
        for (int i = 0; i < 30 * 20; i++) Assert.Equal(ArRecoveryAction.None, r.Tick(false, false, Frame));
        Assert.Equal(0, r.ResetCount);
        Assert.True(r.MobUsable);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void BadFrameTimesDoNotCount(float dt)
    {
        var r = new ArTrackingRecovery(15f);
        for (int i = 0; i < 100; i++) Assert.Equal(ArRecoveryAction.None, r.Tick(true, false, dt));
        Assert.Equal(0f, r.LostSeconds);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidLimitIsRejected(float limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArTrackingRecovery(limit));
    }

    // #31 P1: A31 picked a plane 2.91 m below a handheld phone.
    [Theory]
    [InlineData(1.24, -1.67, false)] // A31 v0 session: 2.91 m below the camera
    [InlineData(1.24, -1.28, false)] // 2.52 m (1 cm float tolerance, as for the placement distance)
    [InlineData(1.24, -1.26, true)]  // 2.50 m: the limit itself is allowed
    [InlineData(1.30, 0.00, true)]   // normal floor 1.3 m below
    [InlineData(0.00, 0.40, true)]   // surface above the camera: left to the existing plane rules
    [InlineData(double.NaN, 0.0, false)]
    public void PlaneMoreThanLimitBelowCameraIsRejected(double cameraY, double planeY, bool ok)
    {
        double limit = ArPlacementSettings.CreatePrototypeDefaults().maxPlaneDropBelowCameraMeters;
        Assert.Equal(2.5, limit, 6);
        Assert.Equal(ok, ArEncounterFraming.IsPlaneHeightAcceptable(cameraY, planeY, limit));
    }

    [Fact]
    public void PlaneDropIsCameraMinusPlane()
    {
        Assert.Equal(2.91, ArEncounterFraming.PlaneDropBelowCamera(1.24, -1.67), 6);
    }
}
