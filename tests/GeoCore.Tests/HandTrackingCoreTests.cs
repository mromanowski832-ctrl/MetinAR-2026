using System.Numerics;
using MetinAR.HandTracking;
using Xunit;

namespace GeoCore.Tests;

/// <summary>METINAR-007: camera-image geometry, model output decoding and ROI rules of the hand-tracking pipeline.</summary>
public class HandTrackingCoreTests
{
    private static void Near(float expected, float actual, float tol, string what = "")
        => Assert.True(MathF.Abs(expected - actual) <= tol, $"{what} expected {expected} got {actual} (tol {tol})");

    private static void Near(Vector2 expected, Vector2 actual, float tol, string what = "")
        => Assert.True(Vector2.Distance(expected, actual) <= tol, $"{what} expected {expected} got {actual} (tol {tol})");

    [Fact]
    public void Anchors_Count_Order_And_MatchPublishedTableChecksums()
    {
        Vector2[] a = PalmAnchors.Generate();
        Assert.Equal(PalmAnchors.ExpectedCount, a.Length);
        Near(new Vector2(0.5f / 24, 0.5f / 24), a[0], 1e-6f);
        Near(a[0], a[1], 1e-7f);
        Near(new Vector2(1.5f / 24, 0.5f / 24), a[2], 1e-6f);
        Near(new Vector2(0.5f / 12, 0.5f / 12), a[1152], 1e-6f);
        Near(new Vector2(11.5f / 12, 11.5f / 12), a[2015], 1e-6f);

        // Order-sensitive checksums of the anchor table published with the models (anchors.csv, HF revision 1e4394f).
        double sx = 0, sy = 0, wx = 0, wy = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sx += a[i].X;
            sy += a[i].Y;
            wx += a[i].X * (double)i;
            wy += a[i].Y * (double)i;
        }

        Assert.InRange(sx, 1007.99, 1008.01);
        Assert.InRange(sy, 1007.99, 1008.01);
        Assert.InRange(wx, 1025307.0, 1025309.0);
        Assert.InRange(wy, 1187735.0, 1187737.0);
    }

    [Fact]
    public void Affine_Multiply_Inverse_And_RotationConvention()
    {
        Affine2D m = Affine2D.Translation(10, -4) * Affine2D.Rotation(0.7f) * Affine2D.Scale(2.5f, 2.5f);
        var p = new Vector2(3, 7);
        Near(p, m.Inverse().Apply(m.Apply(p)), 1e-4f, "roundtrip");
        Affine2D r = Affine2D.Rotation(0.3f);
        Near(new Vector2(MathF.Sin(0.3f), -MathF.Cos(0.3f)), r.ApplyVector(new Vector2(0, -1)), 1e-6f, "crop up");
        Assert.Throws<InvalidOperationException>(() => Affine2D.Scale(0, 1).Inverse());
    }

    [Fact]
    public void PalmDecoder_PicksBestAnchor_DecodesRelativeToAnchor_AndThresholds()
    {
        Vector2[] anchors = PalmAnchors.Generate();
        var scores = new float[anchors.Length];
        var boxes = new float[anchors.Length * PalmDetectionDecoder.ValuesPerAnchor];
        Array.Fill(scores, -8f);
        scores[100] = float.NaN;
        int best = 1500;
        scores[best] = 3f;
        int o = best * 18;
        boxes[o] = 2f;
        boxes[o + 1] = -3f;
        boxes[o + 2] = 40f;
        boxes[o + 3] = 36f;
        boxes[o + 4] = -5f; // kp0 (wrist)
        boxes[o + 5] = 12f;
        boxes[o + 8] = 1f; // kp2 (middle MCP)
        boxes[o + 9] = -14f;

        Assert.True(PalmDetectionDecoder.TryDecodeBest(boxes, scores, anchors, 192, 0.5f, out PalmDetection d));
        Assert.Equal(best, d.AnchorIndex);
        Near(PalmDetectionDecoder.Sigmoid(3f), d.Score, 1e-6f);
        Vector2 anchorPx = anchors[best] * 192;
        Near(anchorPx + new Vector2(2, -3), d.BoxCenter, 1e-4f);
        Near(40f, d.BoxWidth, 1e-6f);
        Near(anchorPx + new Vector2(-5, 12), d.Keypoints[0], 1e-4f);
        Near(anchorPx + new Vector2(1, -14), d.Keypoints[2], 1e-4f);

        scores[best] = -1f; // sigmoid ≈ 0.27 < 0.5
        Assert.False(PalmDetectionDecoder.TryDecodeBest(boxes, scores, anchors, 192, 0.5f, out _));
        Near(1f, PalmDetectionDecoder.Sigmoid(1e6f), 1e-6f);
        Near(0f, PalmDetectionDecoder.Sigmoid(-1e6f), 1e-6f);
    }

    [Fact]
    public void RoiFromPalm_UprightHand_ShiftsTowardFingers_AndScales()
    {
        var det = new PalmDetection
        {
            BoxCenter = new Vector2(100, 100), BoxWidth = 20, BoxHeight = 18,
            Keypoints = new[] { new Vector2(100, 110), Vector2.Zero, new Vector2(100, 90), Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero },
        };
        Affine2D toImage = Affine2D.Scale(2f, 2f); // tensor → image ×2
        HandRoi roi = HandRoiCalculator.FromPalm(det, toImage);
        Near(0f, roi.Rotation, 1e-5f, "rotation");
        Near(20f * 2f * 2.6f, roi.Size, 1e-3f, "size");
        Near(new Vector2(200, 200 - 0.5f * 40f), roi.Center, 1e-3f, "center");

        // Hand pointing right in the image: crop must be rotated +90° so the fingers are "up" in the crop.
        det.Keypoints[0] = new Vector2(90, 100);
        det.Keypoints[2] = new Vector2(110, 100);
        roi = HandRoiCalculator.FromPalm(det, toImage);
        Near(MathF.PI / 2, roi.Rotation, 1e-5f, "rotation 90");
        Near(new Vector2(200 + 20f, 200), roi.Center, 1e-3f, "shift right");
        Affine2D crop = roi.TensorToImage(224);
        Near(roi.Center, crop.Apply(112, 112), 1e-3f, "crop centre");
        Vector2 cropUp = Vector2.Normalize(crop.ApplyVector(new Vector2(0, -1)));
        Near(new Vector2(1, 0), cropUp, 1e-5f, "crop up = fingers");
    }

    [Fact]
    public void RoiFromLandmarks_ContainsHand_AndFollowsRotation()
    {
        var proj = CameraProjection.FromFieldOfView(60f, 0.75f);
        var cam = new Vector3[21];
        var vp = new Vector2[21];
        var world = new Vector3[21];
        // Hand upright facing the camera at 0.5 m; image = viewport × 1000 with y down.
        SyntheticHand.CameraPoints(new Vector3(0, -0.05f, 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI), cam);
        SyntheticHand.Observe(cam, proj, vp, world);
        var img = vp.Select(v => new Vector2(v.X * 1000, (1 - v.Y) * 1000)).ToArray();
        HandRoi roi = HandRoiCalculator.FromLandmarks(img);
        Near(0f, roi.Rotation, 0.15f, "upright rotation");
        Affine2D toTensor = roi.TensorToImage(224).Inverse();
        foreach (Vector2 p in img)
        {
            Vector2 t = toTensor.Apply(p);
            Assert.InRange(t.X, 0, 224);
            Assert.InRange(t.Y, 0, 224);
        }

        // Rotate the image 90°: ROI rotation follows.
        var rotated = img.Select(p => new Vector2(1000 - p.Y, p.X)).ToArray();
        HandRoi roi2 = HandRoiCalculator.FromLandmarks(rotated);
        Near(roi.Rotation + MathF.PI / 2, roi2.Rotation, 1e-3f, "rotated");
        Near(roi.Size, roi2.Size, 0.5f, "size invariant");
    }

    [Fact]
    public void Sampler_BilinearIdentity_BlackOutside_AndRotation()
    {
        int w = 8, h = 4;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                rgba[i] = (byte)(x * 30);
                rgba[i + 1] = (byte)(y * 60);
                rgba[i + 2] = 255;
                rgba[i + 3] = 255;
            }
        }

        var dst = new float[4 * 4 * 3];
        HandImageSampler.SampleRgb(rgba, w, h, w * 4, Affine2D.Identity, 4, dst);
        Near(0f, dst[0], 1e-6f);
        Near(90f / 255f, dst[(2 * 4 + 3) * 3], 1e-5f, "R at (3,2)");
        Near(120f / 255f, dst[(2 * 4 + 3) * 3 + 1], 1e-5f, "G at (3,2)");
        Near(1f, dst[2], 1e-6f, "B");

        // Shift by +100 px: everything outside → black.
        HandImageSampler.SampleRgb(rgba, w, h, w * 4, Affine2D.Translation(100, 0), 4, dst);
        Assert.All(dst, v => Assert.Equal(0f, v));

        // Half-pixel interpolation between x=1 and x=2.
        HandImageSampler.SampleRgb(rgba, w, h, w * 4, Affine2D.Translation(1.5f, 0), 4, dst);
        Near(45f / 255f, dst[0], 1e-5f, "bilinear");
        Assert.Throws<ArgumentException>(() => HandImageSampler.SampleRgb(new byte[10], w, h, w * 4, Affine2D.Identity, 4, dst));
    }

    [Fact]
    public void Mapper_IdentityDisplay_TopLeftIsViewportTopLeft_AndRoundTrips()
    {
        var m = CameraImageViewportMapper.Identity(640, 480);
        Near(new Vector2(0, 1), m.ImageToViewport(new Vector2(0, 0)), 1e-6f, "top-left");
        Near(new Vector2(1, 0), m.ImageToViewport(new Vector2(640, 480)), 1e-6f, "bottom-right");
        Near(new Vector2(123, 45), m.ViewportToImagePx(m.ImageToViewport(new Vector2(123, 45))), 1e-3f);
        Affine2D lb = m.UprightLetterbox(192);
        Near(new Vector2(0, -80), lb.Apply(0, 0), 1e-3f, "letterbox origin");
        Near(new Vector2(640, 560), lb.Apply(192, 192), 1e-3f, "letterbox end");
    }

    [Fact]
    public void Mapper_PortraitDisplayMatrix_UprightLetterboxFollowsScreen()
    {
        // Portrait phone, landscape sensor image (640×480): screen up = image +x, screen right = image +y, with
        // a centre crop of the sensor image in x (s = 0.125 + 0.75·v, t = u).
        var m = new CameraImageViewportMapper(m00: 0f, m01: 1f, m10: 0.75f, m11: 0f, m20: 0.125f, m21: 0f, 640, 480);
        Vector2 screenUpInImage = Vector2.Normalize(m.ViewportToImage.ApplyVector(new Vector2(0, 1)));
        Near(new Vector2(1, 0), screenUpInImage, 1e-5f, "screen up");
        Near(new Vector2(0.125f * 640, 0), m.ViewportToImagePx(new Vector2(0, 0)), 1e-3f, "viewport origin");

        Affine2D lb = m.UprightLetterbox(192);
        Near(new Vector2(1, 0), Vector2.Normalize(lb.ApplyVector(new Vector2(0, -1))), 1e-5f, "tensor up = screen up");
        Near(new Vector2(0, 1), Vector2.Normalize(lb.ApplyVector(new Vector2(1, 0))), 1e-5f, "tensor right = screen right");
        Affine2D inv = lb.Inverse();
        foreach (var corner in new[] { new Vector2(0, 0), new Vector2(640, 0), new Vector2(0, 480), new Vector2(640, 480) })
        {
            Vector2 t = inv.Apply(corner);
            Assert.InRange(t.X, -1e-3f, 192.001f);
            Assert.InRange(t.Y, -1e-3f, 192.001f);
        }

        // Row-flip hypothesis mirrors the image rows.
        var flipped = new CameraImageViewportMapper(0f, 1f, 0.75f, 0f, 0.125f, 0f, 640, 480, flipImageRows: true);
        Near(new Vector2(0.125f * 640, 480), flipped.ViewportToImagePx(new Vector2(0, 0)), 1e-3f, "flipped origin");
    }

    [Fact]
    public void LandmarkDecoder_ProjectsCropPointsToImage_ScalesDepth_RotatesWorld()
    {
        var roi = new HandRoi(new Vector2(300, 200), 448, MathF.PI / 2);
        var lm = new float[63];
        var world = new float[63];
        lm[0] = 112; lm[1] = 112; lm[2] = 0;             // centre
        lm[3] = 112; lm[4] = 0; lm[5] = -10;             // top of crop (fingers side)
        world[3] = 0.0f; world[4] = -0.05f; world[5] = 0.01f;
        var frame = new HandLandmarkFrame();
        HandLandmarkDecoder.Decode(lm, world, 0.97f, 0.2f, roi, frame);
        Near(new Vector2(300, 200), frame.ImagePoint2D(0), 1e-3f, "centre");
        Near(new Vector2(300 + 224, 200), frame.ImagePoint2D(1), 1e-3f, "crop up → image right at 90°");
        Near(-20f, frame.ImagePoints[1].Z, 1e-4f, "z scaled by roi/224");
        Near(0.05f, frame.WorldPoints[1].X, 1e-6f, "world rotated");
        Near(0f, frame.WorldPoints[1].Y, 1e-6f);
        Near(0.97f, frame.Presence, 0f);
    }
}
