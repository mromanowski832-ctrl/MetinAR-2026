using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MetinAR.ArEncounter;
using Xunit;

namespace MetinAR.GeoCore.Tests;

// #97 P0 "Bezpieczeństwo AR: walka bez chodzenia" — the 7 regression tests (pure, engine-free).
// Owner rules: walking only on the GPS map; no walking / approaching / backing away in AR; the mob is stable in space but may
// move by itself; out of reach the mob comes to the player; movement is smooth (no teleport); never so close it fills the
// screen; tracking loss pauses movement + combat and the camera-relative pose returns with HP kept; reach = blade geometry.
// R_h = 1.21 m is the blade reach the Unity build logs for the FPP sword pose (FppHandsView.SwordPose is engine-only).
public class ArNoWalkRegressionTests
{
    private const double UnityBladeReach = 1.21;
    private const double CameraHeight = 1.2;
    private static readonly double Scale = ArEncounterFraming.MobVisualScale;

    private static readonly ArNoWalkSettings Settings = new ArNoWalkSettings();
    private static double StopDistance => ArMobStandoff.StopDistance(UnityBladeReach, Settings, Scale, out _);

    private static ArYawPose Camera(double x = 0, double z = 0, double yaw = 0) => new ArYawPose(x, CameraHeight, z, yaw);

    /// <summary>Mob placed on the floor straight ahead of a camera at the origin looking along +Z, facing the camera.</summary>
    private static ArYawPose MobAhead(double distance) => new ArYawPose(0, 0, distance, 180);

    private static ArNoWalkEncounter NewEncounter() =>
        new ArNoWalkEncounter(Settings, new ArTrackingRecovery(ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds), StopDistance);

    private sealed class Run
    {
        public readonly List<ArNoWalkFrame> Frames = new List<ArNoWalkFrame>();
        public readonly List<double> Distances = new List<double>();
        public ArYawPose Mob;
        public ArYawPose Cam;
    }

    /// <summary>Plays frames like the scene controller: the returned pose is applied to the mob (Restore included).</summary>
    private static Run Simulate(ArNoWalkEncounter enc, ArYawPose cam, ArYawPose mob, double seconds, double dt,
                                Func<double, bool> tracking = null, Func<double, ArYawPose> cameraAt = null, bool canMove = true)
    {
        var run = new Run { Mob = mob, Cam = cam };
        for (double t = 0; t < seconds; t += dt)
        {
            run.Cam = cameraAt?.Invoke(t) ?? cam;
            ArNoWalkFrame f = enc.Tick(tracking?.Invoke(t) ?? true, run.Cam, run.Mob, dt, canMove);
            if (f.Action == ArNoWalkAction.Restore) enc.MarkRestored();
            run.Mob = f.MobPose;
            run.Frames.Add(f);
            run.Distances.Add(ArYawPose.HorizontalDistance(run.Cam, run.Mob));
        }

        return run;
    }

    // ---- 1. static camera, mob out of reach: the mob comes to the player, the player never has to move ----

    [Theory]
    [InlineData(3.0)]
    [InlineData(2.2)]
    [InlineData(1.6)]
    public void T1_StaticCamera_OutOfReachMobApproachesOnItsOwn(double placedAt)
    {
        var enc = NewEncounter();
        ArYawPose cam = Camera();
        Run run = Simulate(enc, cam, MobAhead(placedAt), 8.0, 1.0 / 60);

        double reach = UnityBladeReach;
        Assert.True(ArApproachPrompt.IsOutOfReach(placedAt - ArMobStandoff.HitFrontOffsetMeters(Scale), reach));
        Assert.Contains(run.Frames, f => f.Motion == ArMobMotion.Approaching);
        for (int i = 1; i < run.Distances.Count; i++)
        {
            Assert.True(run.Distances[i] <= run.Distances[i - 1] + 1e-9, "approach must not back off");
        }

        double final = run.Distances[run.Distances.Count - 1];
        Assert.InRange(final, StopDistance - Settings.stopToleranceMeters, StopDistance + Settings.stopToleranceMeters);
        // In reach now without the camera ever moving.
        Assert.False(ArApproachPrompt.IsOutOfReach(final - ArMobStandoff.HitFrontOffsetMeters(Scale), reach));
        Assert.Equal(cam.X, run.Cam.X);
        Assert.Equal(cam.Z, run.Cam.Z);
        // The mob turns to face the camera.
        Assert.True(Math.Abs(ArMobApproach.DeltaAngle(run.Mob.YawDegrees, 180)) < 1.0);
    }

    // ---- 2. no teleport: every frame step is bounded, also over hitches, bad dt and a tracking loss + session reset ----

    [Theory]
    [InlineData(1.0 / 120)]
    [InlineData(1.0 / 60)]
    [InlineData(1.0 / 30)]
    [InlineData(0.25)]
    [InlineData(2.0)]
    public void T2_NoTeleport_StepPerFrameIsBounded(double dt)
    {
        var enc = NewEncounter();
        Run run = Simulate(enc, Camera(), MobAhead(3.0), 12.0, dt);
        Assert.Equal(0.04, Settings.MaxStepMeters, 9);
        Assert.All(run.Frames, f => Assert.True(f.StepMeters <= Settings.MaxStepMeters + 1e-9));
        for (int i = 1; i < run.Distances.Count; i++)
        {
            Assert.True(Math.Abs(run.Distances[i] - run.Distances[i - 1]) <= Settings.MaxStepMeters + 1e-9);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.5)]
    [InlineData(0.0)]
    public void T2_NoTeleport_InvalidDeltaTimeDoesNotMove(double dt)
    {
        var approach = new ArMobApproach(Settings, StopDistance);
        ArMobStep step = approach.Tick(0, 0, 0, 3.0, 180, dt, true);
        Assert.Equal(0.0, step.StepMeters);
        Assert.Equal(3.0, step.Z);
    }

    [Fact]
    public void T2_NoTeleport_TrackingLossAndResetRestoreCameraRelativeWithoutJump()
    {
        var enc = NewEncounter();
        float reset = ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds;
        ArYawPose cam0 = Camera();
        // The player turns and shifts while tracking is lost (the new session has a new world frame).
        ArYawPose cam1 = Camera(0.4, -0.3, 75);
        double lostFrom = 1.0, lostTo = 1.0 + reset + 2.0;
        Run run = Simulate(enc, cam0, MobAhead(2.4), lostTo + 6.0, 1.0 / 60,
                           tracking: t => t < lostFrom || t >= lostTo,
                           cameraAt: t => t < lostFrom ? cam0 : cam1);

        Assert.Contains(run.Frames, f => f.Action == ArNoWalkAction.ResetSession);
        ArNoWalkFrame restore = run.Frames.Single(f => f.Action == ArNoWalkAction.Restore);
        Assert.True(restore.StepMeters <= Settings.MaxStepMeters, "camera-relative jump on restore");
        int ri = run.Frames.IndexOf(restore);
        // Camera-relative distance right before the loss == right after the restore.
        int before = (int)(lostFrom * 60) - 1;
        Assert.Equal(run.Distances[before], run.Distances[ri], 6);
        for (int i = ri + 1; i < run.Distances.Count; i++)
        {
            Assert.True(Math.Abs(run.Distances[i] - run.Distances[i - 1]) <= Settings.MaxStepMeters + 1e-9);
        }
    }

    // ---- 3. the mob stops at the stand-off distance and holds still (no creeping, no oscillation) ----

    [Fact]
    public void T3_StopsAtStandoffAndHolds()
    {
        Assert.Equal(1.32, StopDistance, 2);  // R_h 1.21 − m 0.20 + f 0.345×0.9
        var enc = NewEncounter();
        Run run = Simulate(enc, Camera(), MobAhead(3.0), 20.0, 1.0 / 60);
        int settled = run.Frames.FindIndex(f => f.Motion == ArMobMotion.Holding && f.StepMeters == 0 &&
                                                run.Frames.IndexOf(f) > 10);
        Assert.True(settled > 0);
        double rest = run.Distances[settled];
        Assert.InRange(rest, StopDistance - Settings.stopToleranceMeters, StopDistance + Settings.stopToleranceMeters);
        for (int i = settled; i < run.Frames.Count; i++)
        {
            Assert.Equal(ArMobMotion.Holding, run.Frames[i].Motion);
            Assert.Equal(0.0, run.Frames[i].StepMeters);
            Assert.Equal(rest, run.Distances[i], 9);
        }

        Assert.Equal(0.0, enc.Approach.Speed);
    }

    [Fact]
    public void T3_SmallCameraSwayInsideHysteresisDoesNotRestartApproach()
    {
        var enc = NewEncounter();
        Run run = Simulate(enc, Camera(), MobAhead(StopDistance), 10.0, 1.0 / 60,
                           cameraAt: t => Camera(0.0, -0.06 * Math.Sin(t * 2.0)));  // ±6 cm hand sway (< 0.15 m hysteresis)
        Assert.All(run.Frames, f => Assert.Equal(0.0, f.StepMeters));
    }

    // ---- 4. never closer than d_min: not by its own walking, not by placement, not by restore ----

    [Fact]
    public void T4_OwnMotionNeverBringsTheMobCloserThanDMin()
    {
        for (double d0 = Settings.minDistanceMeters; d0 <= 3.0 + 1e-9; d0 += 0.05)
        {
            foreach (double dt in new[] { 1.0 / 60, 0.1, 1.0 })
            {
                Run run = Simulate(NewEncounter(), Camera(), MobAhead(d0), 10.0, dt);
                Assert.All(run.Distances, d => Assert.True(d >= Settings.minDistanceMeters - 1e-9, $"d0={d0} dt={dt} d={d}"));
            }
        }
    }

    [Fact]
    public void T4_PlayerLeaningInBelowDMin_MobBacksOffSmoothlyToStandoff()
    {
        // The player leans in so the mob is at 0.9 m (the camera cannot be stopped; the mob can only react).
        Run run = Simulate(NewEncounter(), Camera(), MobAhead(0.9), 6.0, 1.0 / 60);
        Assert.Equal(ArMobMotion.Retreating, run.Frames[0].Motion);
        for (int i = 1; i < run.Distances.Count; i++)
        {
            Assert.True(run.Distances[i] >= run.Distances[i - 1] - 1e-9, "while too close the mob never comes closer");
            Assert.True(run.Frames[i].StepMeters <= Settings.MaxStepMeters + 1e-9);
        }

        Assert.InRange(run.Distances[run.Distances.Count - 1], StopDistance - Settings.stopToleranceMeters,
                       StopDistance + Settings.stopToleranceMeters);
    }

    [Fact]
    public void T4_PlacementAndRestoreRespectDMin()
    {
        Assert.Equal(ArEncounterFraming.MinPlacementDistanceMeters, Settings.minDistanceMeters, 9);
        Assert.Equal(ArSpawnDistance.TooClose, ArEncounterFraming.ClassifySpawnDistance(0, 0, 0, 1.05,
            ArEncounterFraming.MinPlacementDistanceMeters, ArEncounterFraming.MaxPlacementDistanceMeters));
        var recorded = ArCameraRelativePose.Capture(Camera(), MobAhead(0.8));
        ArYawPose restored = recorded.Restore(Camera(1, 1, 90), Settings.minDistanceMeters, Settings.maxRestoreDistanceMeters);
        Assert.Equal(Settings.minDistanceMeters, ArYawPose.HorizontalDistance(Camera(1, 1, 90), restored), 9);
        // A configuration whose stand-off would fall below d_min is clamped up (and the scene logs CLAMPED).
        Assert.Equal(Settings.minDistanceMeters, ArMobStandoff.StopDistance(0.8, Settings, Scale, out bool clamped), 9);
        Assert.True(clamped);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArMobApproach(Settings, 1.0));
    }

    // ---- 5. tracking loss pauses movement + combat; restore returns the camera-relative pose ----

    [Fact]
    public void T5_TrackingLoss_PausesMotionAndCombat_ShortLossKeepsWorldPose()
    {
        var enc = NewEncounter();
        double lostFrom = 0.5, lostTo = 1.5;  // shorter than the reset limit
        Run run = Simulate(enc, Camera(), MobAhead(3.0), 3.0, 1.0 / 60, tracking: t => t < lostFrom || t >= lostTo);
        int a = (int)(lostFrom * 60) + 1, b = (int)(lostTo * 60) - 1;
        for (int i = a; i <= b; i++)
        {
            ArNoWalkFrame f = run.Frames[i];
            Assert.Equal(ArMobMotion.Paused, f.Motion);
            Assert.False(f.CombatAllowed);
            Assert.False(f.MobVisible);
            Assert.False(f.MobMoved);
            Assert.Equal(0.0, f.StepMeters);
            Assert.Equal(run.Distances[a - 1], run.Distances[i], 12);
        }

        // After the short loss the approach resumes smoothly from a standstill (speed ramps from 0 again).
        Assert.True(run.Frames[b + 2].CombatAllowed);
        Assert.Equal(ArMobMotion.Approaching, run.Frames[b + 3].Motion);
        Assert.True(run.Frames[b + 2].StepMeters < 0.02);
    }

    [Fact]
    public void T5_SessionReset_RestoresDistanceBearingHeightAndFacingRelativeToTheNewCamera()
    {
        var enc = NewEncounter();
        float reset = ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds;
        // Mob off to the right, already holding; camera yaw 20°.
        ArYawPose cam0 = Camera(0, 0, 20);
        double heading = (20 + 25) * Math.PI / 180;
        var mob0 = new ArYawPose(Math.Sin(heading) * 1.4, 0, Math.Cos(heading) * 1.4, 20 + 25 + 180);
        Simulate(enc, cam0, mob0, 0.5, 1.0 / 60);
        ArCameraRelativePose before = enc.LastRelativePose;

        ArYawPose cam1 = new ArYawPose(-2.0, 0.9, 5.0, -140);  // new session frame / player turned around while lost
        Run lost = Simulate(enc, cam1, mob0, reset + 1.0, 1.0 / 60, tracking: _ => false);
        Assert.Contains(lost.Frames, f => f.Action == ArNoWalkAction.ResetSession);
        Assert.All(lost.Frames, f => Assert.False(f.CombatAllowed));

        ArNoWalkFrame restore = enc.Tick(true, cam1, mob0, 1.0 / 60, true);
        Assert.Equal(ArNoWalkAction.Restore, restore.Action);
        Assert.False(restore.CombatAllowed);
        enc.MarkRestored();
        ArCameraRelativePose after = ArCameraRelativePose.Capture(cam1, restore.MobPose);
        Assert.Equal(before.DistanceMeters, after.DistanceMeters, 9);
        Assert.Equal(before.BearingDegrees, after.BearingDegrees, 6);
        Assert.Equal(before.DropBelowCameraMeters, after.DropBelowCameraMeters, 9);
        Assert.Equal(0.0, ArMobApproach.DeltaAngle(before.YawOffsetDegrees, after.YawOffsetDegrees), 6);
        Assert.Equal(1, enc.RestoreCount);

        ArNoWalkFrame next = enc.Tick(true, cam1, restore.MobPose, 1.0 / 60, true);
        Assert.True(next.CombatAllowed);
        Assert.True(next.MobVisible);
    }

    // ---- 6. reach = blade geometry: the stand-off is a real hit with the FPP hit volume ----

    /// <summary>
    /// Straight blade from 0.3 m in front of the camera to its horizontal reach R (pitched down by <paramref name="pitchDeg"/>),
    /// against the reach column of a mob at <paramref name="d"/> facing the camera, in mob-local unscaled coordinates.
    /// </summary>
    private static bool BladeHits(double d, double reach, double pitchDeg)
    {
        double p = pitchDeg * Math.PI / 180;
        FppPoint Local(double along)
        {
            double worldZ = along * Math.Cos(p), worldY = CameraHeight - 0.25 - along * Math.Sin(p);
            return new FppPoint(0, worldY / Scale, (d - worldZ) / Scale);  // mob faces −Z: local +Z = towards the camera
        }

        return FppHitGeometry.IntersectsReachColumn(Local(0.3), Local(reach));
    }

    [Fact]
    public void T6_StandoffIsDerivedFromBladeGeometryAndIsAHit()
    {
        double f = ArMobStandoff.HitFrontOffsetMeters(Scale);
        Assert.Equal(FppHitGeometry.ReachColumnMax.Z * Scale, f, 12);
        foreach (double reach in new[] { 1.0, 1.21, 1.4, 1.6 })
        {
            double stop = ArMobStandoff.StopDistance(reach, Settings, Scale, out bool clamped);
            if (!clamped) Assert.Equal(reach - Settings.reachMarginMeters + f, stop, 12);
            Assert.True(BladeHits(stop, reach, 0), $"R={reach}: stand-off must be a hit");
            // The whole hold band (up to where the approach restarts) is still a hit.
            Assert.True(BladeHits(stop + Settings.approachHysteresisMeters, reach, 0));
            // And the distance-only reach diagnostic agrees with the geometry.
            Assert.False(ArApproachPrompt.IsOutOfReach(stop - f, reach));
        }
    }

    [Fact]
    public void T6_HitBoundaryMatchesReachPlusFrontOffset()
    {
        double f = ArMobStandoff.HitFrontOffsetMeters(Scale);
        for (double d = 1.0; d <= 2.2; d += 0.01)
        {
            bool hit = BladeHits(d, UnityBladeReach, 0);
            if (Math.Abs(d - (UnityBladeReach + f)) > 0.005) Assert.Equal(d - UnityBladeReach <= f, hit);
            if (Math.Abs(d - f - UnityBladeReach) > 0.005)
                Assert.Equal(!hit, ArApproachPrompt.IsOutOfReach(d - f, UnityBladeReach));
        }

        // Pitching the phone shortens the horizontal reach; at d_stop the blade still lands up to 30° pitch.
        Assert.True(BladeHits(StopDistance, UnityBladeReach * Math.Cos(30 * Math.PI / 180), 0));
        // Placement far edge (3.0 m) is out of reach -> the mob has to come (rule 4), not the player.
        Assert.False(BladeHits(ArEncounterFraming.MaxPlacementDistanceMeters, UnityBladeReach, 0));
    }

    [Fact]
    public void T6_BladeReachIsTheLongestCameraLocalBladeLength()
    {
        // Aim-independent: the longest camera-local blade sample is the horizontal reach at a level aim (upper bound otherwise).
        var blade = new[] { new FppPoint(0.2, -0.3, 0.3), new FppPoint(0.1, -0.4, 1.0), new FppPoint(0.0, -0.5, 1.2) };
        Assert.Equal(1.3, ArApproachPrompt.BladeReach(blade), 9);
    }

    // ---- 7. no UI text tells the player to walk, approach or back away in AR ----

    private static readonly string[] ForbiddenWalkWords =
    {
        "podejdź", "podejdz", "odsuń", "odsun", "cofnij", "przejdź", "przejdz", "zbliż się", "zbliz sie", "idź", "idz do",
        "chodź", "chodz", "krok", "oddal się", "walk", "approach", "step back", "move closer",
    };

    private static void AssertNoWalkInstruction(string text, string where)
    {
        string lower = (text ?? string.Empty).ToLowerInvariant();
        foreach (string w in ForbiddenWalkWords)
        {
            Assert.False(Regex.IsMatch(lower, @"(^|[^\p{L}])" + Regex.Escape(w) + @"($|[^\p{L}])"), $"{where}: \"{text}\" contains \"{w}\"");
        }
    }

    [Fact]
    public void T7_EveryHudStatusIsFreeOfWalkInstructions()
    {
        int checkedTexts = 0;
        foreach (ArEncounterState state in Enum.GetValues(typeof(ArEncounterState)))
        foreach (ArMobMotion motion in Enum.GetValues(typeof(ArMobMotion)))
        foreach (bool target in new[] { false, true })
        foreach (bool tooClose in new[] { false, true })
        foreach (bool tooFar in new[] { false, true })
        foreach (bool hidden in new[] { false, true })
        foreach (bool limited in new[] { false, true })
        foreach (float lost in new[] { 0f, 11f })
        {
            AssertNoWalkInstruction(ArEncounterStatusText.For(state, target, lost, "Jadowity pająk", tooClose, tooFar, hidden, limited, motion),
                                    $"{state}/{motion}");
            checkedTexts++;
        }

        Assert.True(checkedTexts > 500);
        Assert.Equal("Jadowity pająk nadchodzi.", ArEncounterStatusText.For(ArEncounterState.Placed, false, 0, "Jadowity pająk",
            false, false, false, false, ArMobMotion.Approaching));
        Assert.Equal(ArEncounterStatusText.SearchingSurroundings, ArEncounterStatusText.For(ArEncounterState.Placed, false, 0, "X",
            false, false, false, true, ArMobMotion.Paused));
        Assert.Contains("1,1 m", ArEncounterStatusText.PlacementTooClose);
        Assert.Contains("3,0 m", ArEncounterStatusText.PlacementTooFar);
    }

    [Fact]
    public void T7_NoStringLiteralInTheArClientTellsThePlayerToWalk()
    {
        string root = FindRepoRoot();
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(root, "client/Assets/MetinAR/Scripts/ArEncounter"), "*.cs"));
        files.AddRange(Directory.GetFiles(Path.Combine(root, "client/Assets/MetinAR/Scenes"), "*.unity", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).StartsWith("Ar", StringComparison.Ordinal)));
        Assert.Contains(files, p => p.EndsWith("ArEncounterStatusText.cs", StringComparison.Ordinal));
        var literal = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"");
        int literals = 0;
        foreach (string file in files)
        {
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal)) continue;
                if (file.EndsWith(".unity", StringComparison.Ordinal))
                {
                    if (line.StartsWith("m_text:", StringComparison.Ordinal)) { AssertNoWalkInstruction(line.Substring(7), file); literals++; }
                    continue;
                }

                string code = raw.Contains("//") && !raw.Contains("\"") ? raw.Substring(0, raw.IndexOf("//", StringComparison.Ordinal)) : raw;
                foreach (Match m in literal.Matches(code))
                {
                    // Log lines ("[ArEncounter] mob motion Holding->Approaching …") are diagnostics, not player instructions.
                    // Log lines ("[ArEncounter] no-walk standoff …") are diagnostics, not player-facing text.
                    if (m.Value.StartsWith("\"[", StringComparison.Ordinal)) continue;
                    if (code.Contains("Debug.Log") || code.Contains("LogWarning") || code.Contains("LogError")) continue;
                    AssertNoWalkInstruction(m.Value, Path.GetFileName(file));
                    literals++;
                }
            }
        }

        Assert.True(literals > 20, $"only {literals} literals scanned");
    }

    private static string FindRepoRoot()
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
