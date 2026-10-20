using System.Numerics;
using MetinAR.HandTracking;
using Xunit;

namespace GeoCore.Tests;

/// <summary>METINAR-007: hand pose → sword pose mapping, smoothing, and sword hit gating.</summary>
public class HandSwordTests
{
    private static readonly CameraProjection Proj = CameraProjection.FromFieldOfView(62f, 1080f / 1920f);

    private static float AngleDeg(Vector3 a, Vector3 b)
        => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    private static (Vector2[] vp, Vector3[] world, Vector3[] cam) Hand(Vector3 wrist, Quaternion rot)
    {
        var cam = new Vector3[21];
        var vp = new Vector2[21];
        var world = new Vector3[21];
        SyntheticHand.CameraPoints(wrist, rot, cam);
        SyntheticHand.Observe(cam, Proj, vp, world);
        return (vp, world, cam);
    }

    public static IEnumerable<object[]> Poses()
    {
        yield return new object[] { 0.0f, -0.05f, 0.45f, 0f, 0f, 0f };
        yield return new object[] { 0.10f, 0.05f, 0.60f, 0f, 0f, 40f };
        yield return new object[] { -0.08f, -0.12f, 0.35f, 25f, -30f, -70f };
        yield return new object[] { 0.05f, 0.00f, 0.80f, -20f, 50f, 120f };
    }

    [Theory]
    [MemberData(nameof(Poses))]
    public void PoseMapping_RecoversGripPositionAndBladeDirection(float x, float y, float z, float pitch, float yaw, float roll)
    {
        Quaternion rot = Quaternion.CreateFromYawPitchRoll(yaw * MathF.PI / 180f, pitch * MathF.PI / 180f, roll * MathF.PI / 180f);
        var (vp, world, cam) = Hand(new Vector3(x, y, z), rot);
        var est = new HandPoseEstimator { GripMode = SwordGripMode.Fist };
        HandPose pose = est.Estimate(vp, world, Proj);
        Assert.True(pose.Valid);
        Vector3 expectedGrip = SyntheticHand.GripCentre(cam);
        Assert.True(Vector3.Distance(expectedGrip, pose.GripPosition) < 0.01f + 0.02f * z,
            $"grip {pose.GripPosition} vs {expectedGrip}");
        Vector3 expectedBlade = cam[HandLandmark.IndexMcp] - cam[HandLandmark.PinkyMcp];
        Assert.True(AngleDeg(expectedBlade, pose.BladeDirection) < 10f, $"blade angle {AngleDeg(expectedBlade, pose.BladeDirection)}");
        float centreDepth = cam.Average(p => p.Z);
        Assert.True(MathF.Abs(pose.Depth - centreDepth) < 0.03f * centreDepth + 0.005f, $"depth {pose.Depth} vs {centreDepth}");

        // Rotation is consistent with the reported axes (+Z = blade, +Y = up).
        Assert.True(AngleDeg(Vector3.Transform(Vector3.UnitZ, pose.GripRotation), pose.BladeDirection) < 0.5f);
        Assert.True(AngleDeg(Vector3.Transform(Vector3.UnitY, pose.GripRotation), pose.Up) < 0.5f);

        est.GripMode = SwordGripMode.Pointing;
        HandPose pointing = est.Estimate(vp, world, Proj);
        Vector3 across = Vector3.Normalize(expectedBlade);
        Vector3 expectedAlong = cam[HandLandmark.MiddleMcp] - cam[HandLandmark.Wrist];
        expectedAlong -= Vector3.Dot(expectedAlong, across) * across;
        Assert.True(AngleDeg(expectedAlong, pointing.BladeDirection) < 10f, $"pointing angle {AngleDeg(expectedAlong, pointing.BladeDirection)}");
    }

    [Fact]
    public void PoseMapping_SwordFollowsRealHandTranslationAndRotation()
    {
        var est = new HandPoseEstimator();
        var (vpA, wA, _) = Hand(new Vector3(-0.1f, -0.05f, 0.5f), Quaternion.Identity);
        HandPose a = est.Estimate(vpA, wA, Proj);
        var (vpB, wB, _) = Hand(new Vector3(0.15f, 0.05f, 0.5f), Quaternion.Identity);
        HandPose b = est.Estimate(vpB, wB, Proj);
        Vector3 moved = b.GripPosition - a.GripPosition;
        Assert.True(Vector3.Distance(new Vector3(0.25f, 0.10f, 0f), moved) < 0.02f, $"moved {moved}");

        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4);
        var (vpC, wC, _) = Hand(new Vector3(-0.1f, -0.05f, 0.5f), turn);
        HandPose c = est.Estimate(vpC, wC, Proj);
        Assert.InRange(AngleDeg(a.BladeDirection, c.BladeDirection), 38f, 52f);
    }

    [Fact]
    public void PoseMapping_FallbackWithoutWorldLandmarks_AndInvalidInput()
    {
        var est = new HandPoseEstimator();
        var (vp, _, _) = Hand(new Vector3(0, 0, 0.5f), Quaternion.Identity);
        HandPose pose = est.Estimate(vp, null, Proj);
        Assert.True(pose.Valid);
        Assert.InRange(pose.Depth, 0.35f, 0.65f);

        vp[3] = new Vector2(float.NaN, 0.5f);
        Assert.False(est.Estimate(vp, null, Proj).Valid);
        Assert.False(est.Estimate(new Vector2[5], null, Proj).Valid);

        // Implausibly tiny hand → clamped to the max depth and flagged.
        var (far, farWorld, _) = Hand(new Vector3(0, 0, 6f), Quaternion.Identity);
        HandPose clamped = est.Estimate(far, farWorld, Proj);
        Assert.True(clamped.Valid && clamped.DepthClamped);
        Assert.Equal(est.MaxDepth, clamped.Depth, 3);
    }

    [Fact]
    public void LookRotation_MatchesUnityConvention()
    {
        Quaternion q = HandPoseEstimator.LookRotation(new Vector3(1, 0, 0), Vector3.UnitY);
        Assert.True(Vector3.Distance(new Vector3(1, 0, 0), Vector3.Transform(Vector3.UnitZ, q)) < 1e-5f);
        Assert.True(Vector3.Distance(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, q)) < 1e-5f);
        // Unity: LookRotation(right, up) turns +X (right) to −Z (back).
        Assert.True(Vector3.Distance(new Vector3(0, 0, -1), Vector3.Transform(Vector3.UnitX, q)) < 1e-5f);
    }

    [Fact]
    public void ProjectionRoundTrip()
    {
        var p = new Vector3(0.12f, -0.07f, 0.6f);
        Vector2 vp = Proj.CameraToViewport(p);
        Vector2 unit = Proj.ViewportToUnitDepth(vp);
        Assert.True(Vector2.Distance(new Vector2(p.X / p.Z, p.Y / p.Z), unit) < 1e-5f);
    }

    [Fact]
    public void Smoother_ReducesJitter_TracksMotion_HoldsThenHides_SnapsOnJump()
    {
        var smoother = new HandPoseSmoother();
        var rng = new Random(7);
        float rawVar = 0, smoothVar = 0;
        var centre = new Vector3(0, 0, 0.5f);
        for (int i = 0; i < 120; i++)
        {
            float t = i / 30f;
            var noise = new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * 0.01f;
            var s = smoother.Update(t, new HandPose { Valid = true, GripPosition = centre + noise, GripRotation = Quaternion.Identity });
            if (i > 30)
            {
                rawVar += noise.LengthSquared();
                smoothVar += (s.GripPosition - centre).LengthSquared();
            }
        }

        Assert.True(smoothVar < 0.5f * rawVar, $"jitter not reduced: {smoothVar} vs {rawVar}");

        // A steady 1 m/s motion is followed with little lag.
        float t0 = 4f;
        SmoothedHandPose last = default;
        for (int i = 0; i < 15; i++)
        {
            float t = t0 + i / 30f;
            last = smoother.Update(t, new HandPose { Valid = true, GripPosition = centre + new Vector3(i / 30f, 0, 0), GripRotation = Quaternion.Identity });
        }

        Assert.True(14f / 30f - last.GripPosition.X < 0.08f, $"lag {14f / 30f - last.GripPosition.X}");

        float tLost = t0 + 15 / 30f;
        var held = smoother.Update(tLost + 0.1f, new HandPose { Valid = false });
        Assert.True(held.Visible && held.Holding);
        var hidden = smoother.Update(tLost + 0.4f, new HandPose { Valid = false });
        Assert.False(hidden.Visible);

        // Re-acquired far away: snaps immediately (no smear across the room).
        var re = smoother.Update(tLost + 0.5f, new HandPose { Valid = true, GripPosition = new Vector3(1, 1, 1), GripRotation = Quaternion.Identity });
        Assert.True(re.Visible && Vector3.Distance(new Vector3(1, 1, 1), re.GripPosition) < 1e-5f);
    }

    // ------------------------------------------------------------------ hit gating

    private static SwordHitDecision Step(SwordHitGate g, ref float t, ref Vector3 tip, Vector3 velocity, bool contact, bool tracked = true, float dt = 1f / 30f)
    {
        t += dt;
        tip += velocity * dt;
        return g.Update(t, tip, tracked, contact);
    }

    private static void Settle(SwordHitGate g, ref float t, ref Vector3 tip, int frames = 10)
    {
        for (int i = 0; i < frames; i++)
        {
            Step(g, ref t, ref tip, Vector3.Zero, contact: false);
        }
    }

    [Fact]
    public void HitGate_FastSwingThroughMob_HitsExactlyOncePerSwing()
    {
        var g = new SwordHitGate();
        float t = 0;
        var tip = Vector3.Zero;
        Settle(g, ref t, ref tip);
        var v = new Vector3(2.5f, 0, 0);
        Assert.Equal(SwordHitDecision.NoContact, Step(g, ref t, ref tip, v, false));
        Assert.Equal(SwordHitDecision.NoContact, Step(g, ref t, ref tip, v, false));
        Assert.Equal(SwordHitDecision.Hit, Step(g, ref t, ref tip, v, true));
        Assert.Equal(SwordHitDecision.RejectedSameSwing, Step(g, ref t, ref tip, v, true));
        Assert.Equal(SwordHitDecision.RejectedSameSwing, Step(g, ref t, ref tip, v, true));
        Assert.Equal(1, g.HitCount);
        Assert.True(g.SwingActive);
    }

    [Fact]
    public void HitGate_SlowPushThroughMob_IsNotAHit()
    {
        var g = new SwordHitGate();
        float t = 0;
        var tip = Vector3.Zero;
        Settle(g, ref t, ref tip);
        var slow = new Vector3(0.4f, 0, 0);
        for (int i = 0; i < 20; i++)
        {
            SwordHitDecision d = Step(g, ref t, ref tip, slow, contact: i > 3);
            Assert.NotEqual(SwordHitDecision.Hit, d);
        }

        Assert.Equal(SwordHitDecision.RejectedTooSlow, g.LastDecision);
        Assert.Equal(0, g.HitCount);
    }

    [Fact]
    public void HitGate_NewSwingAfterSlowdown_HitsAgain_ButCooldownApplies()
    {
        var g = new SwordHitGate();
        float t = 0;
        var tip = Vector3.Zero;
        Settle(g, ref t, ref tip);
        var v = new Vector3(0, -3f, 0);
        Step(g, ref t, ref tip, v, false);
        Step(g, ref t, ref tip, v, false);
        Assert.Equal(SwordHitDecision.Hit, Step(g, ref t, ref tip, v, true));

        // Stop for 0.33 s (swing ends), then an immediate new fast swing: still inside the 0.5 s cooldown.
        for (int i = 0; i < 10; i++)
        {
            Step(g, ref t, ref tip, Vector3.Zero, false);
        }

        Assert.False(g.SwingActive);
        Step(g, ref t, ref tip, -v, false);
        Step(g, ref t, ref tip, -v, false);
        Assert.Equal(SwordHitDecision.RejectedCooldown, Step(g, ref t, ref tip, -v, true));

        // Wait out the cooldown, swing again → second hit.
        Settle(g, ref t, ref tip, 15);
        Step(g, ref t, ref tip, v, false);
        Step(g, ref t, ref tip, v, false);
        Assert.Equal(SwordHitDecision.Hit, Step(g, ref t, ref tip, v, true));
        Assert.Equal(2, g.HitCount);
    }

    [Fact]
    public void HitGate_TrackingJumpLossAndReacquisition_NeverHit()
    {
        var g = new SwordHitGate();
        float t = 0;
        var tip = Vector3.Zero;
        Settle(g, ref t, ref tip);

        // Re-detection teleports the tip 0.8 m in one sample, landing inside the mob.
        t += 1f / 30f;
        tip += new Vector3(0.8f, 0, 0);
        Assert.Equal(SwordHitDecision.RejectedTrackingJump, g.Update(t, tip, true, true));

        // Hand lost while touching the mob.
        Assert.Equal(SwordHitDecision.RejectedNotTracked, Step(g, ref t, ref tip, Vector3.Zero, true, tracked: false));

        // Re-acquired and immediately fast in contact: guarded for minTrackedSeconds.
        var v = new Vector3(3f, 0, 0);
        Assert.Equal(SwordHitDecision.RejectedJustAcquired, Step(g, ref t, ref tip, v, true));
        Assert.Equal(SwordHitDecision.RejectedJustAcquired, Step(g, ref t, ref tip, v, true));
        Assert.Equal(0, g.HitCount);
    }

    [Fact]
    public void Geometry_SegmentBox_AndSweptBladeDoesNotTunnel()
    {
        var c = new Vector3(0, 0.3f, 0);
        var half = new Vector3(0.6f, 0.3f, 0.5f);
        Assert.True(SwordGeometry.SegmentIntersectsBox(new Vector3(-1, 0.3f, 0), new Vector3(1, 0.3f, 0), c, half));
        Assert.False(SwordGeometry.SegmentIntersectsBox(new Vector3(-1, 1.0f, 0), new Vector3(1, 1.0f, 0), c, half));
        Assert.True(SwordGeometry.SegmentIntersectsBox(new Vector3(0, 0.2f, 0), new Vector3(0.1f, 0.25f, 0), c, half), "fully inside");
        Assert.False(SwordGeometry.SegmentIntersectsBox(new Vector3(0.7f, 0.3f, 0), new Vector3(0.7f, 0.3f, 0.2f), c, half), "parallel outside");

        // Blade above the box, then below it one frame later: the swept test catches the pass-through.
        var hiltA = new Vector3(-0.2f, 1.0f, 0);
        var tipA = new Vector3(0.2f, 1.0f, 0);
        var hiltB = new Vector3(-0.2f, -0.5f, 0);
        var tipB = new Vector3(0.2f, -0.5f, 0);
        Assert.False(SwordGeometry.SegmentIntersectsBox(hiltA, tipA, c, half));
        Assert.False(SwordGeometry.SegmentIntersectsBox(hiltB, tipB, c, half));
        Assert.True(SwordGeometry.SweptBladeHitsBox(hiltA, tipA, hiltB, tipB, 0.02f, c, half));
        Assert.False(SwordGeometry.SweptBladeHitsBox(hiltA + Vector3.UnitX * 3, tipA + Vector3.UnitX * 3, hiltB + Vector3.UnitX * 3, tipB + Vector3.UnitX * 3, 0.02f, c, half));
    }
}
