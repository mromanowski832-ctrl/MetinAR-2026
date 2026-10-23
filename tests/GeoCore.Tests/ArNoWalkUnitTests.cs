using System;
using MetinAR.ArEncounter;
using Xunit;

namespace MetinAR.GeoCore.Tests;

// #97 supporting unit tests: config validation, stand-off formula, approach dynamics, camera-relative pose, gait.
public class ArNoWalkUnitTests
{
    [Fact]
    public void DefaultSettingsMatchTheAgreedValues()
    {
        var s = new ArNoWalkSettings();
        s.Validate();
        Assert.Equal(0.20, s.reachMarginMeters);
        Assert.Equal(1.10, s.minDistanceMeters);
        Assert.Equal(0.15, s.approachHysteresisMeters);
        Assert.Equal(0.05, s.stopToleranceMeters);
        Assert.Equal(0.6, s.approachSpeedMetersPerSecond);
        Assert.Equal(0.8, s.maxSpeedMetersPerSecond);
        Assert.Equal(3.0, s.AccelerationMetersPerSecond2, 9);
        Assert.Equal(180.0, s.turnDegreesPerSecond);
        Assert.Equal(0.04, s.MaxStepMeters, 9);
        Assert.Equal(ArEncounterFraming.MaxPlacementDistanceMeters, s.maxRestoreDistanceMeters);
        Assert.Equal(0.9, s.mobVisualScale, 6);
    }

    [Fact]
    public void MobVisualScale_IsConfig_Default09_AndStandoffReadsIt()
    {
        // Lead decision 2026-10-23 04:40: scale 0.9 stays, but in configuration (owner may ask for ~0.6).
        var s = new ArNoWalkSettings();
        Assert.Equal(0.9, s.mobVisualScale, 6);
        Assert.Equal(ArEncounterFraming.DefaultMobVisualScale, s.mobVisualScale);
        Assert.Equal(1.21 - 0.20 + 0.345 * 0.9, ArMobStandoff.StopDistance(1.21, s, out bool c1), 9);
        Assert.False(c1);
        s.mobVisualScale = 0.6;
        s.Validate();
        Assert.Equal(1.21 - 0.20 + 0.345 * 0.6, ArMobStandoff.StopDistance(1.21, s, out bool c2), 9);
        Assert.False(c2);
        Assert.Equal(ArMobStandoff.StopDistance(1.21, s, 0.6, out _), ArMobStandoff.StopDistance(1.21, s, out _), 12);
        s.mobVisualScale = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Validate());
    }

    [Theory]
    [InlineData("min")]
    [InlineData("speed")]
    [InlineData("speedAboveMax")]
    [InlineData("accel")]
    [InlineData("margin")]
    [InlineData("restore")]
    [InlineData("nan")]
    public void InvalidSettingsAreRejected(string which)
    {
        var s = new ArNoWalkSettings();
        switch (which)
        {
            case "min": s.minDistanceMeters = 0; break;
            case "speed": s.approachSpeedMetersPerSecond = -1; break;
            case "speedAboveMax": s.approachSpeedMetersPerSecond = 1.0; break;
            case "accel": s.accelerationSeconds = 0; break;
            case "margin": s.reachMarginMeters = -0.1; break;
            case "restore": s.maxRestoreDistanceMeters = 1.0; break;
            case "nan": s.maxStepSeconds = double.NaN; break;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArMobApproach(s, 1.5));
    }

    [Fact]
    public void StandoffFormula()
    {
        Assert.Equal(1.21 - 0.20 + 0.345 * 0.9, ArMobStandoff.RawStopDistance(1.21, 0.20, 0.345 * 0.9), 12);
        Assert.Equal(1.3205, ArMobStandoff.StopDistance(1.21, new ArNoWalkSettings(), 0.9, out bool clamped), 9);
        Assert.False(clamped);
    }

    [Fact]
    public void ApproachAcceleratesSmoothlyCruisesAndDeceleratesWithoutOvershoot()
    {
        var a = new ArMobApproach(new ArNoWalkSettings(), 1.32);
        double z = 3.0, yaw = 180, prevSpeed = 0, maxSpeed = 0;
        for (int i = 0; i < 600; i++)
        {
            ArMobStep s = a.Tick(0, 0, 0, z, yaw, 1.0 / 60, true);
            Assert.True(s.Speed - prevSpeed <= 3.0 / 60 + 1e-9, "acceleration bounded");
            Assert.True(s.Z >= 1.32 - 1e-9, "never overshoots the stop distance");
            maxSpeed = Math.Max(maxSpeed, s.Speed);
            prevSpeed = s.Speed;
            z = s.Z;
            yaw = s.YawDegrees;
        }

        Assert.Equal(0.6, maxSpeed, 6);
        Assert.Equal(1.32, z, 6);
        Assert.Equal(3.0 - 1.32, a.TravelledMeters, 6);
    }

    [Fact]
    public void TurnRateIsLimited()
    {
        var a = new ArMobApproach(new ArNoWalkSettings(), 1.32);
        ArMobStep s = a.Tick(0, 0, 0, 2.0, 0, 0.1, true);  // facing away, target yaw 180; dt clamped to 0.05 s
        Assert.Equal(9.0, Math.Abs(ArMobApproach.DeltaAngle(s.YawDegrees, 0)), 6);
    }

    [Fact]
    public void PausedWhenNotAllowedOrInputInvalid()
    {
        var a = new ArMobApproach(new ArNoWalkSettings(), 1.32);
        Assert.Equal(ArMobMotion.Paused, a.Tick(0, 0, 0, 3.0, 180, 0.1, false).Motion);
        Assert.Equal(ArMobMotion.Paused, a.Tick(double.NaN, 0, 0, 3.0, 180, 0.1, true).Motion);
        Assert.Equal(0.0, a.Speed);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, 2.0, 180.0)]
    [InlineData(1.0, 2.0, 45.0, 2.0, 3.5, 1.0, -30.0)]
    [InlineData(-1.0, 0.5, -170.0, -2.5, -0.2, 0.3, 90.0)]
    public void CameraRelativePoseRoundTrips(double cx, double cz, double cyaw, double mx, double mz, double my, double myaw)
    {
        var cam = new ArYawPose(cx, 1.3, cz, cyaw);
        var mob = new ArYawPose(mx, my, mz, myaw);
        ArCameraRelativePose rel = ArCameraRelativePose.Capture(cam, mob);
        Assert.True(rel.IsValid);
        ArYawPose back = rel.Restore(cam, 0.0, 100.0);
        Assert.Equal(mx, back.X, 9);
        Assert.Equal(my, back.Y, 9);
        Assert.Equal(mz, back.Z, 9);
        Assert.Equal(0.0, ArMobApproach.DeltaAngle(back.YawDegrees, myaw), 6);
        Assert.Throws<InvalidOperationException>(() => default(ArCameraRelativePose).Restore(cam, 1, 3));
    }

    [Fact]
    public void GaitAlternatesLegSetsAndIsStillAtZeroWeight()
    {
        Assert.True(ArMobGait.IsSetA(true, 0));
        Assert.False(ArMobGait.IsSetA(false, 0));
        Assert.False(ArMobGait.IsSetA(true, 1));
        Assert.True(ArMobGait.IsSetA(false, 1));
        double phase = ArMobGait.Phase(0.11);
        Assert.Equal(-ArMobGait.LegSwing(phase, true, 0, 1), ArMobGait.LegSwing(phase, false, 0, 1), 9);
        Assert.Equal(0.0, ArMobGait.LegSwing(phase, true, 0, 0));
        Assert.Equal(0.0, ArMobGait.Bob(phase, 0));
        for (double t = 0; t < 1; t += 0.01)
        {
            Assert.InRange(ArMobGait.LegLift(ArMobGait.Phase(t), true, 2, 1), 0.0, ArMobGait.LiftMeters + 1e-12);
            Assert.InRange(Math.Abs(ArMobGait.LegSwing(ArMobGait.Phase(t), false, 3, 1)), 0.0, ArMobGait.SwingDegrees + 1e-12);
        }
    }
}
