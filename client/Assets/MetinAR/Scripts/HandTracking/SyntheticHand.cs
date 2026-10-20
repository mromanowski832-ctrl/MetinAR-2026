using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// Deterministic synthetic hand (approximate adult right hand, metres) used by the tests and Play Mode smoke test to
    /// produce landmark observations exactly like the camera pipeline would: viewport landmarks + metric world landmarks.
    /// Hand-local frame: wrist at the origin, fingers toward +Y, index finger toward +X, palm facing −Z.
    /// </summary>
    public static class SyntheticHand
    {
        public static readonly Vector3[] LocalPoints = BuildLocal();

        private static Vector3[] BuildLocal()
        {
            var p = new Vector3[HandLandmark.Count];
            p[HandLandmark.Wrist] = Vector3.Zero;
            p[HandLandmark.ThumbCmc] = new Vector3(0.025f, 0.025f, -0.01f);
            p[HandLandmark.ThumbMcp] = new Vector3(0.045f, 0.045f, -0.015f);
            p[HandLandmark.ThumbIp] = new Vector3(0.060f, 0.065f, -0.02f);
            p[HandLandmark.ThumbTip] = new Vector3(0.072f, 0.085f, -0.025f);
            AddFinger(p, HandLandmark.IndexMcp, new Vector3(0.030f, 0.085f, 0f), 0.040f, 0.025f, 0.020f);
            AddFinger(p, HandLandmark.MiddleMcp, new Vector3(0.008f, 0.090f, 0f), 0.045f, 0.028f, 0.022f);
            AddFinger(p, HandLandmark.RingMcp, new Vector3(-0.013f, 0.085f, 0f), 0.040f, 0.026f, 0.020f);
            AddFinger(p, HandLandmark.PinkyMcp, new Vector3(-0.033f, 0.075f, 0f), 0.030f, 0.020f, 0.018f);
            return p;
        }

        private static void AddFinger(Vector3[] p, int mcp, Vector3 basePoint, float a, float b, float c)
        {
            p[mcp] = basePoint;
            p[mcp + 1] = basePoint + new Vector3(0f, a, 0f);
            p[mcp + 2] = basePoint + new Vector3(0f, a + b, 0f);
            p[mcp + 3] = basePoint + new Vector3(0f, a + b + c, 0f);
        }

        /// <summary>Hand points in camera space for a wrist position and hand rotation (hand-local → camera).</summary>
        public static void CameraPoints(Vector3 wristCamera, Quaternion rotation, Vector3[] result)
        {
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                result[i] = wristCamera + Vector3.Transform(LocalPoints[i], rotation);
            }
        }

        /// <summary>
        /// Projects camera-space hand points into the observation format: viewport points and world landmarks
        /// (metric, centred, image axes: y down, z away from the camera).
        /// </summary>
        public static void Observe(Vector3[] cameraPoints, CameraProjection projection, Vector2[] viewport, Vector3[] world)
        {
            Vector3 mean = Vector3.Zero;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                mean += cameraPoints[i];
            }

            mean /= HandLandmark.Count;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                viewport[i] = projection.CameraToViewport(cameraPoints[i]);
                Vector3 d = cameraPoints[i] - mean;
                world[i] = new Vector3(d.X, -d.Y, d.Z);
            }
        }

        /// <summary>Expected grip centre for given camera-space hand points (same rule as <see cref="HandPoseEstimator"/>).</summary>
        public static Vector3 GripCentre(Vector3[] cameraPoints)
        {
            Vector3 knuckles = 0.25f * (cameraPoints[HandLandmark.IndexMcp] + cameraPoints[HandLandmark.MiddleMcp]
                                        + cameraPoints[HandLandmark.RingMcp] + cameraPoints[HandLandmark.PinkyMcp]);
            return 0.4f * cameraPoints[HandLandmark.Wrist] + 0.6f * knuckles;
        }
    }
}
