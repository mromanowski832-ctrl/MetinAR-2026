using System;
using System.Collections.Generic;
using MetinAR.ArEncounter;
using Xunit;

/// <summary>
/// #31 owner retest on A31 (2026-10-21): sword too big/too close (blade over the centre, guard full width),
/// spider placed too close, nameplate huge up close. Projection uses the ESTIMATED A31 portrait FOV plus a
/// wider/narrower band, because the physical FOV is not measured yet.
/// </summary>
public class FppFramingTests
{
    private static readonly double[] VerticalFovs = { 60.0, FppViewFraming.EstimatedA31VerticalFovDegrees, 75.0 };
    private static readonly double[] Aspects = { FppViewFraming.EstimatedA31PortraitAspect, 9.0 / 16.0 };

    private static (double x, double y) Ndc(FppPoint pos, FppPoint euler, FppPoint local, double fov, double aspect)
    {
        Assert.True(FppViewFraming.TryProject(FppViewFraming.GripToCamera(pos, euler, local), fov, aspect, out double x, out double y));
        return (x, y);
    }

    private static IEnumerable<FppPoint> BladeSamples()
    {
        for (int i = 0; i <= 40; i++)
            yield return new FppPoint(0, FppViewFraming.BladeBaseY + (FppViewFraming.BladeTipY - FppViewFraming.BladeBaseY) * i / 40.0, 0);
    }

    private static FppPoint Guard(double side) => new FppPoint(
        side * FppViewFraming.ModelGuardHalfWidth * FppViewFraming.SwordScale, FppViewFraming.ModelGuardY * FppViewFraming.SwordScale, 0);

    [Fact]
    public void RotateMatchesUnityEulerOrder()
    {
        // Quaternion.Euler(0, 90, 0) * forward = right; Euler(90, 0, 0) * up = forward; Euler(0, 0, 90) * right = up.
        FppPoint a = FppViewFraming.Rotate(new FppPoint(0, 90, 0), new FppPoint(0, 0, 1));
        FppPoint b = FppViewFraming.Rotate(new FppPoint(90, 0, 0), new FppPoint(0, 1, 0));
        FppPoint c = FppViewFraming.Rotate(new FppPoint(0, 0, 90), new FppPoint(1, 0, 0));
        Assert.Equal(1, a.X, 6); Assert.Equal(0, a.Z, 6);
        Assert.Equal(1, b.Z, 6); Assert.Equal(0, b.Y, 6);
        Assert.Equal(1, c.Y, 6); Assert.Equal(0, c.X, 6);
        // Y * X * Z order: Z first, then X, then Y.
        FppPoint d = FppViewFraming.Rotate(new FppPoint(90, 90, 0), new FppPoint(0, 1, 0));
        Assert.Equal(1, d.X, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestAndAuraHold_SwordStaysLowerRight_CentreClear(bool aura)
    {
        FppPoint pos = aura ? FppViewFraming.AuraPosition : FppViewFraming.RestPosition;
        FppPoint euler = aura ? FppViewFraming.AuraEuler : FppViewFraming.RestEuler;
        foreach (double fov in VerticalFovs)
            foreach (double aspect in Aspects)
            {
                var grip = Ndc(pos, euler, new FppPoint(0, 0, 0), fov, aspect);
                Assert.InRange(grip.x, 0.3, 0.95);   // fist visible, right half
                Assert.InRange(grip.y, -0.95, -0.2); // lower part
                var left = Ndc(pos, euler, Guard(-1), fov, aspect);
                var right = Ndc(pos, euler, Guard(1), fov, aspect);
                Assert.True(Math.Min(left.x, right.x) > 0.1, $"guard reaches the centre ({left.x:F2}) fov {fov} aspect {aspect:F2}");
                Assert.True(Math.Max(left.y, right.y) < 0, "guard above the horizon line");
                Assert.True(Math.Abs(right.x - left.x) / 2 < (fov == FppViewFraming.EstimatedA31VerticalFovDegrees ? 0.5 : 0.6), $"guard spans {Math.Abs(right.x - left.x) / 2:P0} of the width");
                foreach (FppPoint p in BladeSamples())
                {
                    var b = Ndc(pos, euler, p, fov, aspect);
                    Assert.True(b.x > 0.15, $"blade left of centre: {b.x:F2},{b.y:F2} fov {fov}");
                    Assert.True(Math.Sqrt(b.x * b.x + b.y * b.y) > 0.5, $"blade near centre: {b.x:F2},{b.y:F2} fov {fov}");
                }
            }
    }

    [Fact]
    public void LeftHandAtRest_VisibleLowerLeft_AwayFromCentre()
    {
        foreach (double fov in VerticalFovs)
            foreach (double aspect in Aspects)
            {
                Assert.True(FppViewFraming.TryProject(FppViewFraming.LeftRestPosition, fov, aspect, out double x, out double y));
                Assert.InRange(x, -0.95, -0.2);
                Assert.InRange(y, -0.95, -0.3);
            }
    }

    [Fact]
    public void OldRetestPose_IsRejectedByTheSameChecks()
    {
        // A31 retest APK 2ec7a47: Aura hold (0.02,-0.18,0.52)/(12,0,0) at full scale put the blade on the centre line.
        var oldBladeMid = FppViewFraming.GripToCamera(new FppPoint(0.02, -0.18, 0.52), new FppPoint(12, 0, 0), new FppPoint(0, 0.5, 0));
        Assert.True(FppViewFraming.TryProject(oldBladeMid, FppViewFraming.EstimatedA31VerticalFovDegrees, FppViewFraming.EstimatedA31PortraitAspect, out double x, out _));
        Assert.True(Math.Abs(x) < 0.15);
        var l = FppViewFraming.GripToCamera(new FppPoint(0.02, -0.18, 0.52), new FppPoint(12, 0, 0), new FppPoint(-0.125, 0.12, 0));
        var r = FppViewFraming.GripToCamera(new FppPoint(0.02, -0.18, 0.52), new FppPoint(12, 0, 0), new FppPoint(0.125, 0.12, 0));
        FppViewFraming.TryProject(l, FppViewFraming.EstimatedA31VerticalFovDegrees, FppViewFraming.EstimatedA31PortraitAspect, out double lx, out _);
        FppViewFraming.TryProject(r, FppViewFraming.EstimatedA31VerticalFovDegrees, FppViewFraming.EstimatedA31PortraitAspect, out double rx, out _);
        Assert.True(Math.Abs(rx - lx) / 2 > 0.7, "old guard should span most of the width");
    }

    [Fact]
    public void SwordScale_ShrinksBladeAndLeftGripTogether()
    {
        Assert.InRange(FppViewFraming.SwordScale, 0.5, 0.9);
        Assert.Equal(0.94 * FppViewFraming.SwordScale, FppViewFraming.BladeTipY, 9);
        Assert.Equal(0.13 * FppViewFraming.SwordScale, FppViewFraming.BladeBaseY, 9);
        Assert.Equal(-0.12 * FppViewFraming.SwordScale, FppViewFraming.LeftGripY, 9);
    }

    [Fact]
    public void ProjectRejectsBehindCameraAndBadInput()
    {
        Assert.False(FppViewFraming.TryProject(new FppPoint(0, 0, -1), 60, 0.5, out _, out _));
        Assert.False(FppViewFraming.TryProject(new FppPoint(0, 0, 1), 0, 0.5, out _, out _));
        Assert.False(FppViewFraming.TryProject(new FppPoint(double.NaN, 0, 1), 60, 0.5, out _, out _));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.8, false)]
    [InlineData(0.0, 0.0, 0.7, 0.7, false)]
    [InlineData(0.0, 0.0, 0.0, 1.08, false)]
    [InlineData(0.0, 0.0, 0.0, 1.095, true)]
    [InlineData(0.0, 0.0, 0.0, 1.1, true)]
    [InlineData(2.0, 3.0, 2.0, 5.0, true)]
    [InlineData(0.0, 0.0, double.NaN, 2.0, false)]
    public void PlacementMinimumDistanceIsHorizontal(double cx, double cz, double tx, double tz, bool expected)
    {
        Assert.Equal(expected, ArEncounterFraming.IsPlacementFarEnough(cx, cz, tx, tz));
    }

    [Fact]
    public void PlacementRulesKeepSpiderBodyWellInsideTheScreenAtDMin()
    {
        // #97: placement minimum = d_min 1.10 m (PROTOTYPE), the mob is never closer (placed or walking).
        Assert.Equal(1.10, ArEncounterFraming.MinPlacementDistanceMeters, 9);
        // Owner retests: 1.0 filled the screen, 0.75 was too small -> strictly between them (scale unchanged by #97).
        Assert.InRange(ArEncounterFraming.MobVisualScale, 0.8f, 0.95f);
        // #97 rule 6 (safest reading: the mob must not cover the screen): the hit body (0.57 m wide unscaled) stays within
        // 60% of the estimated A31 portrait width at d_min for a phone held 1.2 m above the floor (≈ 51%).
        double halfWidth = HalfWidthAt(ArEncounterFraming.MinPlacementDistanceMeters);
        double bodyWidth = (FppHitGeometry.ReachColumnMax.X - FppHitGeometry.BodyMin.X) * ArEncounterFraming.MobVisualScale;
        Assert.True(bodyWidth < 2 * halfWidth * 0.60);
        // OPEN QUESTION (#97, owner decision, not changed here): the whole leg span (1.24 m unscaled × 0.9) no longer fits
        // the width at d_min (≈ 110%) or d_stop 1.32 m (≈ 100%); at the #31 minimum 1.5 m it was ≈ 93%.
        Assert.True(1.24 * ArEncounterFraming.MobVisualScale > 2 * HalfWidthAt(1.10) * 0.95);
    }

    private static double HalfWidthAt(double groundDistance)
    {
        double slant = Math.Sqrt(groundDistance * groundDistance + 1.2 * 1.2);
        return slant * Math.Tan(FppViewFraming.EstimatedA31VerticalFovDegrees * Math.PI / 360) * FppViewFraming.EstimatedA31PortraitAspect;
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.5)]
    public void NameplateNeverExceedsMaxAngleUpClose(double distance)
    {
        double s = ArEncounterFraming.NameplateScale(distance);
        Assert.InRange(s, ArEncounterFraming.NameplateMinScale, 1.0);
        Assert.True(ArEncounterFraming.NameplateAngleDegrees(distance, s) <= ArEncounterFraming.NameplateMaxAngleDegrees + 1e-6);
        // Authored size (scale 1) would be far bigger at these distances.
        Assert.True(ArEncounterFraming.NameplateAngleDegrees(distance, 1.0) > ArEncounterFraming.NameplateMaxAngleDegrees);
    }

    [Fact]
    public void NameplateKeepsAuthoredSizeFarAwayAndHandlesBadInput()
    {
        Assert.Equal(1.0, ArEncounterFraming.NameplateScale(20));
        Assert.Equal(ArEncounterFraming.NameplateMinScale, ArEncounterFraming.NameplateScale(0));
        Assert.Equal(ArEncounterFraming.NameplateMinScale, ArEncounterFraming.NameplateScale(double.NaN));
        Assert.True(ArEncounterFraming.NameplateScale(1.0) < ArEncounterFraming.NameplateScale(2.0));
    }

    [Theory]
    [InlineData(0.6, 0.4, 0.6)]   // below the HUD band: unchanged
    [InlineData(0.95, 0.5, 0.80)] // in the HUD band: lowered to the band edge
    [InlineData(0.95, 0.85, 0.89)] // mob itself near the top: never pushed onto the mob
    [InlineData(0.95, double.NaN, 0.80)]
    public void NameplateViewportClamp(double label, double mobTop, double expected)
    {
        Assert.Equal(expected, ArEncounterFraming.ClampNameplateViewportY(label, mobTop), 6);
    }
}
