using System;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>Hand landmarks of one frame in camera-image pixels plus the model's metric "world" landmarks.</summary>
    public sealed class HandLandmarkFrame
    {
        /// <summary>21 landmarks: x, y in image pixels (y down); z = relative depth in image-pixel units (smaller = closer).</summary>
        public readonly Vector3[] ImagePoints = new Vector3[HandLandmark.Count];

        /// <summary>21 metric landmarks (metres) around the hand centre, rotated into image axes (x right, y down, z away).</summary>
        public readonly Vector3[] WorldPoints = new Vector3[HandLandmark.Count];

        /// <summary>Hand-presence confidence 0..1.</summary>
        public float Presence;

        /// <summary>Raw handedness score 0..1 (model convention; not used for gameplay in this prototype).</summary>
        public float Handedness;

        public HandRoi Roi;

        public Vector2 ImagePoint2D(int index) => new Vector2(ImagePoints[index].X, ImagePoints[index].Y);
    }

    /// <summary>
    /// Projects the landmark model output (tensor pixels of the rotated crop) back into camera-image pixels.
    /// Presence and handedness come out of the model as probabilities already.
    /// </summary>
    public static class HandLandmarkDecoder
    {
        public const int LandmarkInputSize = 224;

        public static void Decode(
            ReadOnlySpan<float> landmarks,
            ReadOnlySpan<float> worldLandmarks,
            float presence,
            float handedness,
            HandRoi roi,
            HandLandmarkFrame result,
            int tensorSize = LandmarkInputSize)
        {
            if (landmarks.Length < HandLandmark.Count * 3)
            {
                throw new ArgumentException("Need 63 landmark values.", nameof(landmarks));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            Affine2D toImage = roi.TensorToImage(tensorSize);
            float zScale = roi.Size / tensorSize;
            float c = MathF.Cos(roi.Rotation);
            float s = MathF.Sin(roi.Rotation);
            bool hasWorld = worldLandmarks.Length >= HandLandmark.Count * 3;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                Vector2 p = toImage.Apply(landmarks[3 * i], landmarks[3 * i + 1]);
                result.ImagePoints[i] = new Vector3(p.X, p.Y, landmarks[3 * i + 2] * zScale);
                if (hasWorld)
                {
                    float wx = worldLandmarks[3 * i];
                    float wy = worldLandmarks[3 * i + 1];
                    result.WorldPoints[i] = new Vector3(c * wx - s * wy, s * wx + c * wy, worldLandmarks[3 * i + 2]);
                }
                else
                {
                    result.WorldPoints[i] = Vector3.Zero;
                }
            }

            result.Presence = presence;
            result.Handedness = handedness;
            result.Roi = roi;
        }
    }
}
