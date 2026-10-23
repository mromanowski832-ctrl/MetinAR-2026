using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.ArEncounter;
using Xunit;

namespace MetinAR.GeoCore.Tests;

// #97 / PR #104: lead decisions 2026-10-23 04:40 (scale in config) and 04:42 (open questions Q2–Q9), pure and engine-free.
public class ArNoWalkLeadDecisionsTests
{
    private const double Reach = 1.21;
    private const double Dt = 1.0 / 60;
    private static readonly float ResetAfter = ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds;

    private static ArYawPose Cam(double x = 0, double z = 0, double yaw = 0) => new ArYawPose(x, 1.2, z, yaw);
    private static ArYawPose MobAt(double d) => new ArYawPose(0, 0, d, 180);

    private static ArNoWalkEncounter New(ArNoWalkSettings s = null)
    {
        s = s ?? new ArNoWalkSettings();
        return new ArNoWalkEncounter(s, new ArTrackingRecovery(ResetAfter), ArMobStandoff.StopDistance(Reach, s, out _));
    }

    private sealed class Sim
    {
        public readonly List<ArNoWalkFrame> Frames = new List<ArNoWalkFrame>();
        public readonly List<ArYawPose> Mobs = new List<ArYawPose>();
        public readonly List<ArYawPose> Cams = new List<ArYawPose>();
        public ArYawPose Mob;
    }

    private static Sim Run(ArNoWalkEncounter enc, ArYawPose mob, double seconds, Func<double, bool> tracking = null,
                           Func<double, ArYawPose> camAt = null, Func<double, double?> planeAt = null, bool canMove = true)
    {
        var sim = new Sim { Mob = mob };
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            double t = i * Dt;
            ArYawPose cam = camAt?.Invoke(t) ?? Cam();
            ArNoWalkFrame f = enc.Tick(tracking?.Invoke(t) ?? true, cam, sim.Mob, Dt, canMove, planeAt?.Invoke(t));
            if (f.Action == ArNoWalkAction.Restore) enc.MarkRestored();
            if (f.Action == ArNoWalkAction.Restore || f.MobMoved) sim.Mob = f.MobPose;
            sim.Frames.Add(f);
            sim.Mobs.Add(sim.Mob);
            sim.Cams.Add(cam);
        }

        return sim;
    }

    private static double Dist3(ArYawPose a, ArYawPose b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Asserts #97 Q5: a visible mob never moves more than the step limit between frames.</summary>
    private static void AssertVisibleMovesSmooth(Sim sim, ArNoWalkSettings s)
    {
        for (int i = 1; i < sim.Frames.Count; i++)
        {
            ArNoWalkFrame f = sim.Frames[i];
            double moved = Dist3(sim.Mobs[i], sim.Mobs[i - 1]);
            if (f.MobVisible)
            {
                Assert.True(moved <= s.StepLimitMeters(Dt) + 1e-9, $"frame {i}: visible mob moved {moved:F4} m");
                Assert.True(f.StepMeters <= s.StepLimitMeters(Dt) + 1e-9, $"frame {i}: step {f.StepMeters:F4} m");
            }
            else if (moved > s.StepLimitMeters(Dt) + 1e-9)
            {
                Assert.False(f.CombatAllowed, $"frame {i}: correction while combat allowed");
            }
        }
    }

    // ---- Q2: restore at once from the recorded camera drop, ease onto a plane within ±0.15 m, combat only at stable Tracking ----

    private static Sim LossResetAndReturn(ArNoWalkEncounter enc, double afterReturn, Func<double, double?> planeAt = null,
                                          Func<double, bool> extraLoss = null)
    {
        double lostFrom = 1.0, lostTo = 1.0 + ResetAfter + 1.0;
        return Run(enc, MobAt(2.4), lostTo + afterReturn,
                   tracking: t => (t < lostFrom || t >= lostTo) && !(extraLoss?.Invoke(t - lostTo) ?? false),
                   camAt: t => t < lostFrom ? Cam() : Cam(0.4, -0.3, 75),
                   planeAt: t => t >= lostTo ? planeAt?.Invoke(t - lostTo) : null);
    }

    [Fact]
    public void Q2_RestoreIsImmediate_FromRecordedCameraDrop_WithoutAPlane()
    {
        var enc = New();
        Sim sim = LossResetAndReturn(enc, 3.0);
        int ri = sim.Frames.FindIndex(f => f.Action == ArNoWalkAction.Restore);
        Assert.True(ri > 0);
        Assert.True(sim.Frames[ri - 1].MobVisible == false);
        // First tracked frame after the reset restores: no waiting for a plane, height = camera − recorded drop (1.2 m).
        Assert.Equal(1.2 - 1.2, sim.Mobs[ri].Y, 9);
        Assert.Equal(sim.Cams[ri].Y - enc.LastRelativePose.DropBelowCameraMeters, sim.Mobs[ri].Y, 9);
        Assert.Equal(1, enc.RestoreCount);
        // No plane ever shows up: the height stays, the settle stays pending, the fight goes on.
        Assert.True(enc.PlaneSettlePending);
        Assert.All(sim.Mobs.Skip(ri), m => Assert.Equal(sim.Mobs[ri].Y, m.Y, 12));
        Assert.True(sim.Frames.Last().CombatAllowed);
    }

    [Theory]
    [InlineData(-0.12)]
    [InlineData(0.15)]
    [InlineData(-0.15)]
    [InlineData(0.05)]
    public void Q2_PlaneWithinTolerance_MobEasesOntoItWithinTheStepLimit(double planeOffset)
    {
        var s = new ArNoWalkSettings();
        var enc = New(s);
        double restoredY = 0.0;  // camera 1.2 m, drop 1.2 m
        Sim sim = LossResetAndReturn(enc, 4.0, planeAt: t => t >= 1.0 ? restoredY + planeOffset : (double?)null);
        Assert.False(enc.PlaneSettlePending);
        Assert.Equal(restoredY + planeOffset, sim.Mob.Y, 9);
        AssertVisibleMovesSmooth(sim, s);
        // It took several frames (no snap): |offset| / 0.0133 m per frame at 60 fps.
        int ri = sim.Frames.FindIndex(f => f.Action == ArNoWalkAction.Restore);
        int moving = 0;
        for (int i = ri + 1; i < sim.Mobs.Count; i++) if (Math.Abs(sim.Mobs[i].Y - sim.Mobs[i - 1].Y) > 1e-12) moving++;
        Assert.True(moving >= (int)Math.Floor(Math.Abs(planeOffset) / s.StepLimitMeters(Dt)), $"eased in {moving} frames");
    }

    [Theory]
    [InlineData(0.30)]
    [InlineData(-0.16)]
    [InlineData(double.NaN)]
    public void Q2_PlaneOutsideTolerance_IsIgnored(double planeOffset)
    {
        var enc = New();
        Sim sim = LossResetAndReturn(enc, 3.0, planeAt: _ => 0.0 + planeOffset);
        Assert.True(enc.PlaneSettlePending);
        Assert.Equal(0.0, sim.Mob.Y, 12);
    }

    [Fact]
    public void Q2_NoSettleWithoutARestore()
    {
        var enc = New();
        Sim sim = Run(enc, MobAt(1.32), 2.0, planeAt: _ => 0.1);
        Assert.False(enc.PlaneSettlePending);
        Assert.All(sim.Mobs, m => Assert.Equal(0.0, m.Y, 12));
    }

    [Fact]
    public void Q2_CombatResumesOnlyAfterStableTracking_AndAFlickerRestartsTheWait()
    {
        var s = new ArNoWalkSettings();
        Assert.Equal(0.5, s.combatResumeStableSeconds);
        var enc = New(s);
        // After the return: tracking flickers off for 2 frames at 0.3 s.
        Sim sim = LossResetAndReturn(enc, 2.0, extraLoss: t => t >= 0.3 && t < 0.3 + 2 * Dt);
        int ri = sim.Frames.FindIndex(f => f.Action == ArNoWalkAction.Restore);
        Assert.False(sim.Frames[ri].CombatAllowed);
        int firstAllowed = sim.Frames.FindIndex(ri, f => f.CombatAllowed);
        double waited = (firstAllowed - ri) * Dt;
        // 0.3 s + 2 lost frames + a full 0.5 s of stable tracking again.
        Assert.True(waited >= 0.3 + 0.5, $"combat resumed after {waited:F2} s");
        Assert.True(waited <= 0.3 + 0.5 + 6 * Dt, $"combat resumed after {waited:F2} s");
        for (int i = firstAllowed; i < sim.Frames.Count; i++) Assert.True(sim.Frames[i].CombatAllowed);
        // A tracked fight that never lost tracking is never delayed.
        var fresh = New(s);
        Assert.True(fresh.Tick(true, Cam(), MobAt(1.32), Dt, true).CombatAllowed);
    }

    [Fact]
    public void Q2_ShortLossWithoutReset_AlsoWaitsForStableTracking()
    {
        var enc = New();
        Sim sim = Run(enc, MobAt(1.32), 3.0, tracking: t => t < 1.0 || t >= 1.5);
        Assert.DoesNotContain(sim.Frames, f => f.Action != ArNoWalkAction.None);
        int back = (int)Math.Round(1.5 / Dt);
        Assert.False(sim.Frames[back].CombatAllowed);
        Assert.True(sim.Frames[back].MobVisible);
        Assert.True(sim.Frames[back + (int)Math.Ceiling(0.5 / Dt) + 1].CombatAllowed);
    }

    // ---- Q5: a correction > 0.04 m only while hidden / tracking-paused; visible = always smooth ----

    [Fact]
    public void Q5_LargeCorrectionOnlyWhileHiddenAndPaused_VisibleAlwaysWithinLimit()
    {
        var s = new ArNoWalkSettings();
        var enc = New(s);
        // The player leans in to 0.9 m and tracking is lost at once (recorded 0.9 < d_min): the restore clamps to 1.10 m,
        // a 0.20 m camera-relative correction, then a plane 0.1 m lower appears and the mob eases onto it.
        double lostFrom = 2 * Dt, lostTo = lostFrom + ResetAfter + 0.5;
        Sim sim = Run(enc, MobAt(0.9), lostTo + 4.0,
                      tracking: t => t < lostFrom || t >= lostTo,
                      camAt: t => t < lostFrom ? Cam() : Cam(0.3, 0.2, 40),
                      planeAt: t => t >= lostTo + 0.5 ? -0.1 : (double?)null, canMove: true);
        ArNoWalkFrame restore = sim.Frames.Single(f => f.Action == ArNoWalkAction.Restore);
        Assert.True(restore.StepMeters > s.MaxStepMeters, $"expected a > 0.04 m correction, got {restore.StepMeters:F3}");
        Assert.False(restore.MobVisible);
        Assert.False(restore.CombatAllowed);
        AssertVisibleMovesSmooth(sim, s);
        foreach (ArNoWalkFrame f in sim.Frames.Where(f => f.StepMeters > s.StepLimitMeters(Dt) + 1e-9))
        {
            Assert.False(f.MobVisible);
            Assert.False(f.CombatAllowed);
        }
    }

    [Theory]
    [InlineData(1.0 / 30)]
    [InlineData(1.0 / 60)]
    [InlineData(0.2)]
    public void Q5_StepLimitIsVmaxTimesClampedDt(double dt)
    {
        var s = new ArNoWalkSettings();
        Assert.Equal(0.8 * Math.Min(dt, 0.05), s.StepLimitMeters(dt), 12);
        Assert.Equal(0.0, s.StepLimitMeters(0));
        Assert.Equal(0.0, s.StepLimitMeters(double.NaN));
        Assert.True(s.StepLimitMeters(dt) <= s.MaxStepMeters + 1e-12);
    }

    // ---- Q6: outside [d_min, reach] after tracking returns → smooth move back to d_stop ----

    [Theory]
    [InlineData(0.85)]  // leaned in during the loss: closer than d_min
    [InlineData(1.00)]
    [InlineData(1.60)]  // stepped back during the loss: beyond reach (d_stop + m = 1.52)
    [InlineData(2.40)]
    public void Q6_AfterShortLoss_MobOutsideTheBandReturnsSmoothlyToDStop(double distanceAfterLoss)
    {
        var s = new ArNoWalkSettings();
        var enc = New(s);
        double dStop = enc.Approach.StopDistanceMeters;
        // Holding at d_stop, tracking lost 1 s (no reset), the camera moved meanwhile.
        Sim sim = Run(enc, MobAt(dStop), 1.0 + 1.0 + 8.0,
                      tracking: t => t < 1.0 || t >= 2.0,
                      camAt: t => t < 1.0 ? Cam() : Cam(0, dStop - distanceAfterLoss));
        int back = (int)Math.Round(2.0 / Dt);
        double atReturn = ArYawPose.HorizontalDistance(sim.Cams[back - 1], sim.Mobs[back - 1]);
        Assert.Equal(distanceAfterLoss, atReturn, 6);  // world pose kept during the short loss
        Assert.DoesNotContain(sim.Frames, f => f.Action != ArNoWalkAction.None);
        AssertVisibleMovesSmooth(sim, s);
        double final = ArYawPose.HorizontalDistance(sim.Cams.Last(), sim.Mobs.Last());
        Assert.InRange(final, dStop - s.stopToleranceMeters, dStop + s.stopToleranceMeters);
        Assert.Equal(ArMobMotion.Holding, sim.Frames.Last().Motion);
        // Never below d_min once it started to retreat; reach band respected at the end.
        Assert.True(final - ArMobStandoff.HitFrontOffsetMeters(s.mobVisualScale) <= Reach);
    }

    [Fact]
    public void Q6_ApproachStartNeverBeyondTheReachBoundary()
    {
        var s = new ArNoWalkSettings();
        var a = new ArMobApproach(s, ArMobStandoff.StopDistance(Reach, s, out _));
        Assert.Equal(a.StopDistanceMeters + 0.15, a.ApproachStartMeters, 12);  // defaults unchanged
        var wide = new ArNoWalkSettings { approachHysteresisMeters = 0.35 };
        var b = new ArMobApproach(wide, ArMobStandoff.StopDistance(Reach, wide, out _));
        // A larger hysteresis is capped at the margin: d_stop + m = R_h + f, the farthest hittable distance.
        Assert.Equal(b.StopDistanceMeters + wide.reachMarginMeters, b.ApproachStartMeters, 12);
        Assert.Equal(Reach + ArMobStandoff.HitFrontOffsetMeters(wide.mobVisualScale), b.ApproachStartMeters, 9);
    }

    // ---- Q3 / Q7: texts ----

    [Fact]
    public void Q3_MobNameIsUsedInTheNominative_WithThirdPersonSingularVerbs()
    {
        string name = ArEncounterRequest.PrototypeTestSpider("test").MobName;
        Assert.Equal("Trujący Pająk", name);
        Assert.Equal("Trujący Pająk nadchodzi.", ArEncounterStatusText.For(ArEncounterState.Placed, false, 0, name,
            false, false, false, false, ArMobMotion.Approaching));
        Assert.Equal("Trujący Pająk stoi na podłożu.", ArEncounterStatusText.For(ArEncounterState.Placed, false, 0, name,
            false, false, false, false, ArMobMotion.Holding));
        // Placement texts carry no mob name, so no inflection is needed there.
        Assert.DoesNotContain(name, ArEncounterStatusText.PlacementTooClose);
        Assert.DoesNotContain(name, ArEncounterStatusText.PlacementTooFar);
        Assert.StartsWith("Za blisko. Unieś telefon", ArEncounterStatusText.PlacementTooClose);
        Assert.StartsWith("Za daleko. Pochyl telefon", ArEncounterStatusText.PlacementTooFar);
    }

    [Fact]
    public void Q7_ScanHintSaysMoveThePhoneWhileStandingStill()
    {
        Assert.Equal("Skanuj teren: poruszaj telefonem, stojąc w miejscu, i celuj w płaskie podłoże.", ArEncounterStatusText.Scanning);
        Assert.Contains("stojąc w miejscu", ArEncounterStatusText.Scanning);
        Assert.DoesNotContain("przesuwaj", ArEncounterStatusText.Scanning);
        Assert.StartsWith(ArEncounterStatusText.Scanning, ArEncounterStatusText.For(ArEncounterState.Scanning, false, 11f, "X",
            false, false, false, false, ArMobMotion.Holding));
    }

    // ---- Q4: per-swing pitch log ----

    [Theory]
    [InlineData(23.44, 1.3205, "[ArReach] swing pitch=23.4 deg dist=1.32 m")]
    [InlineData(-5.0, 2.0, "[ArReach] swing pitch=-5.0 deg dist=2.00 m")]
    [InlineData(0.0, 1.1, "[ArReach] swing pitch=0.0 deg dist=1.10 m")]
    [InlineData(34.06, double.NaN, "[ArReach] swing pitch=34.1 deg dist=n/a")]
    [InlineData(double.NaN, 1.5, "[ArReach] swing pitch=n/a deg dist=1.50 m")]
    public void Q4_SwingReachLogFormat(double pitch, double dist, string expected)
    {
        string line = ArReachLog.Swing(pitch, dist);
        Assert.Equal(expected, line);
        Assert.DoesNotContain("\n", line);
        Assert.Matches(@"^\[ArReach\] swing pitch=(-?\d+\.\d|n/a) deg dist=(\d+\.\d\d m|n/a)$", line);
    }

    [Fact]
    public void Q4_PitchIsPositiveWhenThePhoneTiltsDown_AndCultureInvariant()
    {
        Assert.Equal(30.0, ArReachLog.PitchDegrees(-0.5), 9);
        Assert.Equal(-30.0, ArReachLog.PitchDegrees(0.5), 9);
        Assert.Equal(0.0, ArReachLog.PitchDegrees(0.0), 12);
        Assert.Equal(90.0, ArReachLog.PitchDegrees(-1.2), 9);
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
            Assert.Equal("[ArReach] swing pitch=12.5 deg dist=1.25 m", ArReachLog.Swing(12.5, 1.25));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Q4_SwingLogIsDevOnly_AndCalledOnEverySwing()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "client/Assets/MetinAR/Scripts/ArEncounter/FppCombatController.cs"));
        int log = src.IndexOf("Debug.Log(ArReachLog.Swing(", StringComparison.Ordinal);
        Assert.True(log > 0);
        int gate = src.LastIndexOf("#if UNITY_EDITOR || DEVELOPMENT_BUILD", log, StringComparison.Ordinal);
        int end = src.IndexOf("#endif", log, StringComparison.Ordinal);
        Assert.True(gate > 0 && end > log, "the [ArReach] log must sit inside #if UNITY_EDITOR || DEVELOPMENT_BUILD");
        Assert.Contains("if (started) LogSwingReach();", src);
    }

    // ---- Q9: MobApproachEnabled is on by default and cannot be switched off in a game build ----

    [Fact]
    public void Q9_ApproachGate_DisableOnlyThroughTheTestSeam()
    {
        Assert.True(ArMobApproachGate.Effective(true, false));
        Assert.True(ArMobApproachGate.Effective(true, true));
        Assert.True(ArMobApproachGate.Effective(false, false));   // game build: request ignored
        Assert.False(ArMobApproachGate.Effective(false, true));   // editor smoke test seam
    }

    [Fact]
    public void Q9_ControllerFlagIsNotSerializedNorSettable_AndTheSeamIsEditorOnly()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterSceneController.cs"));
        Assert.Contains("public bool MobApproachEnabled => ArMobApproachGate.Effective(_mobApproachTestRequest, MobApproachTestSeamAvailable);", src);
        Assert.DoesNotContain("MobApproachEnabled { get; set; }", src);
        Assert.Contains("private bool _mobApproachTestRequest = true;", src);
        int field = src.IndexOf("private bool _mobApproachTestRequest", StringComparison.Ordinal);
        string before = src.Substring(Math.Max(0, field - 200), Math.Min(200, field));
        Assert.DoesNotContain("SerializeField", before);
        int seamTrue = src.IndexOf("public const bool MobApproachTestSeamAvailable = true;", StringComparison.Ordinal);
        int seamFalse = src.IndexOf("public const bool MobApproachTestSeamAvailable = false;", StringComparison.Ordinal);
        Assert.True(seamTrue > 0 && seamFalse > seamTrue);
        string block = src.Substring(src.LastIndexOf("#if", seamTrue, StringComparison.Ordinal), seamFalse - src.LastIndexOf("#if", seamTrue, StringComparison.Ordinal));
        Assert.StartsWith("#if UNITY_EDITOR\n", block.Replace("\r\n", "\n"));
        Assert.Contains("#else", block);
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "client/Assets/MetinAR/Scripts/ArEncounter")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }
}
