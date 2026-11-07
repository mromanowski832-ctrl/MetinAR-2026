using MetinAR.ArEncounter;
using Xunit;

public class FppCombatTests
{
    [Fact]
    public void InputsRequireReadyEncounter()
    {
        var t = new FppCombatTimeline();
        Assert.False(t.TryAttack(false));
        Assert.False(t.TryAura(false));
        Assert.Equal(FppCombatAction.Idle, t.Action);
    }

    [Fact]
    public void RepeatedButtonsCannotRestartOrOverlapAttack()
    {
        var t = new FppCombatTimeline();
        Assert.True(t.TryAttack(true));
        t.Tick(0.1f, true);
        for (int i = 0; i < 20; i++)
        {
            Assert.False(t.TryAttack(true));
            Assert.False(t.TryAura(true));
        }
        Assert.Equal(1, t.SwingId);
        Assert.Equal(0.1f, t.Elapsed);
    }

    [Fact]
    public void OnlyActivePartOfSwingCanRegisterOneHitPerTarget()
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(0.1f, true);
        Assert.False(t.TryRecordHit(7));
        t.Tick(0.15f, true);
        Assert.True(t.HasHitWindow);
        Assert.Equal(FppCombatTimeline.HitStartSeconds, t.HitWindowStart);
        Assert.True(t.TryRecordHit(7));
        Assert.False(t.TryRecordHit(7));
        t.Tick(0.1f, true);
        Assert.False(t.TryRecordHit(7));
        t.Tick(0.15f, true);
        Assert.Equal(FppCombatTimeline.HitEndSeconds, t.HitWindowEnd);
        Assert.False(t.TryRecordHit(7));
        t.Tick(0.05f, true);
        Assert.False(t.HasHitWindow);
        Assert.False(t.TryRecordHit(8));
    }

    [Fact]
    public void ExactWindowStartCanHitAndNextSwingRearms()
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(FppCombatTimeline.HitStartSeconds, true);
        Assert.True(t.TryRecordHit(1));
        for (int i = 0; i < 4; i++) t.Tick(0.2f, true);
        Assert.Equal(FppCombatAction.Idle, t.Action);
        Assert.False(t.TryRecordHit(1));
        Assert.True(t.TryAttack(true));
        t.Tick(0.2f, true);
        Assert.True(t.TryRecordHit(1));
        Assert.Equal(2, t.SwingId);
    }

    [Fact]
    public void OrdinaryDroppedFramePreservesCrossedHitInterval()
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(0.17f, true);
        t.Tick(0.25f, true);
        Assert.True(t.HasHitWindow);
        Assert.Equal(FppCombatTimeline.HitStartSeconds, t.HitWindowStart);
        Assert.InRange(t.HitWindowEnd, 0.419f, 0.421f);
        Assert.True(t.TryRecordHit(9));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(0.251f)]
    [InlineData(10f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidOrStalledFrameCancelsWithoutLateHit(float delta)
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(0.2f, true);
        t.Tick(delta, true);
        Assert.Equal(FppCombatAction.Idle, t.Action);
        Assert.False(t.TryRecordHit(1));
        Assert.False(t.AuraActive);
    }

    [Fact]
    public void TrackingLossCancelsAndDoesNotResumeAnOldSwing()
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(0.2f, true);
        t.Tick(0.01f, false);
        t.Tick(0.2f, true);
        Assert.Equal(FppCombatAction.Idle, t.Action);
        Assert.False(t.TryRecordHit(1));
        Assert.True(t.TryAttack(true));
    }

    [Fact]
    public void AuraActivatesAfterGestureNeverHitsAndExpires()
    {
        var t = new FppCombatTimeline();
        Assert.True(t.TryAura(true));
        Assert.False(t.TryAttack(true));
        Assert.False(t.TryAura(true));
        t.Tick(0.25f, true);
        t.Tick(0.25f, true);
        Assert.False(t.AuraActive);
        t.Tick(0.2f, true);
        Assert.True(t.AuraActive);
        Assert.InRange(t.AuraRemaining, 5.94f, 5.96f);
        Assert.False(t.TryRecordHit(1));
        t.Tick(0.25f, true);
        t.Tick(0.25f, true);
        Assert.Equal(FppCombatAction.Idle, t.Action);
        Assert.False(t.TryAura(true));
        Assert.True(t.TryAttack(true));
        for (int i = 0; i < 30; i++) t.Tick(0.2f, true);
        Assert.False(t.AuraActive);
        Assert.True(t.TryAura(true));
    }

    [Fact]
    public void CancelRemovesAuraAndPendingWindows()
    {
        var t = new FppCombatTimeline();
        t.TryAura(true);
        for (int i = 0; i < 4; i++) t.Tick(0.2f, true);
        Assert.True(t.AuraActive);
        t.Cancel();
        Assert.False(t.AuraActive);
        Assert.Equal(FppCombatAction.Idle, t.Action);
        Assert.False(t.TryRecordHit(1));
        Assert.True(t.TryAura(true));
    }

    [Fact]
    public void ZeroDeltaDoesNotCreateHitWindow()
    {
        var t = new FppCombatTimeline();
        t.TryAttack(true);
        t.Tick(0.2f, true);
        t.Tick(0f, true);
        Assert.False(t.TryRecordHit(1));
        Assert.Equal(0.2f, t.Elapsed);
    }

    private static readonly FppPoint Min = new FppPoint(-1, -1, -1);
    private static readonly FppPoint Max = new FppPoint(1, 1, 1);

    [Theory]
    [InlineData(-2, 0, 0, 2, 0, 0, true)]
    [InlineData(2, 0, 0, -2, 0, 0, true)]
    [InlineData(0, 0, -2, 0, 0, 2, true)]
    [InlineData(-2, 2, 0, 2, 2, 0, false)]
    [InlineData(20, 0, 0, 21, 0, 0, false)]
    [InlineData(0, 0, 0, 0, 0, 0, true)]
    [InlineData(2, 2, 2, 2, 2, 2, false)]
    [InlineData(-2, 1, 0, 2, 1, 0, true)]
    [InlineData(-2, -2, -2, -1.1, -1.1, -1.1, false)]
    public void BladeMustActuallyCrossTargetVolume(double ax, double ay, double az, double bx, double by, double bz, bool hit)
    {
        Assert.Equal(hit, FppHitGeometry.IntersectsBox(new FppPoint(ax, ay, az), new FppPoint(bx, by, bz), Min, Max));
    }

    [Fact]
    public void InvalidCoordinatesAndReversedBoundsFailClosed()
    {
        var origin = new FppPoint(0, 0, 0);
        Assert.False(FppHitGeometry.IntersectsBox(new FppPoint(double.NaN, 0, 0), origin, Min, Max));
        Assert.False(FppHitGeometry.IntersectsBox(origin, new FppPoint(0, double.PositiveInfinity, 0), Min, Max));
        Assert.False(FppHitGeometry.IntersectsBox(origin, origin, Max, Min));
    }

    // #31 v0 A31 P0-1: the spider stands on the floor, the blade is held about 1.25 m above it.
    [Theory]
    [InlineData(0, 1.0, 0.2, 0, 1.6, -0.2, true)]     // blade crosses the footprint high above the body: hit
    [InlineData(0, 0.3, 0.2, 0, 0.3, -0.2, true)]     // through the body itself: hit
    [InlineData(0.5, 1.0, 0.0, 0.9, 1.0, 0.0, false)] // beside the footprint: miss
    [InlineData(0, 1.0, 0.6, 0, 1.6, 0.9, false)]     // in front of the footprint: miss
    [InlineData(0, 3.6, 0.0, 0, 4.0, 0.0, false)]     // above the reach column: miss
    public void ReachColumnIsTheBodyFootprintExtrudedUp(double ax, double ay, double az, double bx, double by, double bz, bool hit)
    {
        Assert.Equal(hit, FppHitGeometry.IntersectsReachColumn(new FppPoint(ax, ay, az), new FppPoint(bx, by, bz)));
    }

    [Fact]
    public void ReachColumnKeepsFootprintAndExtendsAboveBodyTop()
    {
        Assert.True(FppHitGeometry.ReachColumnTopY >= 3.0);
        Assert.Equal(0.125, FppHitGeometry.BodyMin.Y);
        Assert.Equal(0.285, FppHitGeometry.ReachColumnMax.X);
        Assert.Equal(0.345, FppHitGeometry.ReachColumnMax.Z);
        // A blade segment 1 m above the floor over the spider centre misses the literal body box but hits the column.
        var a = new FppPoint(-0.2, 1.0, 0); var b = new FppPoint(0.2, 1.0, 0);
        Assert.False(FppHitGeometry.IntersectsBox(a, b, FppHitGeometry.BodyMin, new FppPoint(0.285, FppHitGeometry.BodyTopY, 0.345)));
        Assert.True(FppHitGeometry.IntersectsReachColumn(a, b));
    }
}
