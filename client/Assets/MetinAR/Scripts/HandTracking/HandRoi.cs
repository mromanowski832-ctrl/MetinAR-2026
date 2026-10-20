using System;
using System.Collections.Generic;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>Rotated square region of the camera image (image pixels, y down) fed to the landmark model.</summary>
    public readonly struct HandRoi
    {
        public readonly Vector2 Center;
        public readonly float Size;

        /// <summary>Radians; the crop's "up" (−y) maps to image direction (sin r, −cos r).</summary>
        public readonly float Rotation;

        public HandRoi(Vector2 center, float size, float rotation)
        {
            Center = center;
            Size = size;
            Rotation = rotation;
        }

        public bool IsValid => Size > 1f && !float.IsNaN(Size) && !float.IsNaN(Center.X) && !float.IsNaN(Center.Y) && !float.IsNaN(Rotation);

        /// <summary>Tensor pixels (n×n, y down) → image pixels.</summary>
        public Affine2D TensorToImage(int tensorSize)
        {
            float s = Size / tensorSize;
            return Affine2D.Translation(Center)
                   * Affine2D.Rotation(Rotation)
                   * Affine2D.Scale(s, s)
                   * Affine2D.Translation(-0.5f * tensorSize, -0.5f * tensorSize);
        }

        public override string ToString() => $"ROI(c={Center.X:0.#},{Center.Y:0.#} size={Size:0.#} rot={Rotation * 57.29578f:0.#}°)";
    }

    /// <summary>
    /// Hand region rules (MediaPipe hand-landmarker conventions, implemented from its documentation):
    /// from a palm detection the box is rotated so wrist → middle MCP points up, shifted half a box toward the fingers
    /// and enlarged 2.6×; from previous-frame landmarks the box around the palm joints is enlarged 2.0× and shifted 0.1 up.
    /// </summary>
    public static class HandRoiCalculator
    {
        public const float PalmScale = 2.6f;
        public const float PalmShiftY = -0.5f;
        public const float LandmarkScale = 2.0f;
        public const float LandmarkShiftY = -0.1f;

        public static float NormalizeRadians(float angle)
        {
            float twoPi = 2f * MathF.PI;
            angle %= twoPi;
            if (angle > MathF.PI)
            {
                angle -= twoPi;
            }
            else if (angle < -MathF.PI)
            {
                angle += twoPi;
            }

            return angle;
        }

        /// <summary>Rotation that makes the from → to direction point up in the crop (image y down).</summary>
        public static float RotationFromPoints(Vector2 from, Vector2 to)
        {
            return NormalizeRadians(0.5f * MathF.PI - MathF.Atan2(-(to.Y - from.Y), to.X - from.X));
        }

        private static Vector2 Shift(Vector2 center, float size, float rotation, float shiftY)
        {
            float dy = size * shiftY;
            return center + new Vector2(-dy * MathF.Sin(rotation), dy * MathF.Cos(rotation));
        }

        /// <param name="tensorToImage">Detector tensor px → image px (uniform scale, axis-aligned or 90° rotated).</param>
        public static HandRoi FromPalm(PalmDetection detection, Affine2D tensorToImage)
        {
            Vector2 center = tensorToImage.Apply(detection.BoxCenter);
            Vector2 wrist = tensorToImage.Apply(detection.Keypoints[0]);
            Vector2 middle = tensorToImage.Apply(detection.Keypoints[2]);
            float scale = MathF.Sqrt(MathF.Abs(tensorToImage.Determinant));
            float size = MathF.Max(detection.BoxWidth, detection.BoxHeight) * scale;
            float rotation = RotationFromPoints(wrist, middle);
            center = Shift(center, size, rotation, PalmShiftY);
            return new HandRoi(center, size * PalmScale, rotation);
        }

        /// <param name="imageLandmarks">21 landmarks in image px.</param>
        public static HandRoi FromLandmarks(IReadOnlyList<Vector2> imageLandmarks)
        {
            if (imageLandmarks == null || imageLandmarks.Count < HandLandmark.Count)
            {
                throw new ArgumentException("Need 21 landmarks.", nameof(imageLandmarks));
            }

            float rotation = RotationFromPoints(imageLandmarks[HandLandmark.Wrist], imageLandmarks[HandLandmark.MiddleMcp]);
            Vector2 mean = Vector2.Zero;
            foreach (int i in HandLandmark.RoiSubset)
            {
                mean += imageLandmarks[i];
            }

            mean /= HandLandmark.RoiSubset.Length;

            // Bounding box in the hand-aligned frame (rotate by −rotation around the mean).
            float c = MathF.Cos(-rotation);
            float s = MathF.Sin(-rotation);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (int i in HandLandmark.RoiSubset)
            {
                Vector2 d = imageLandmarks[i] - mean;
                float x = c * d.X - s * d.Y;
                float y = s * d.X + c * d.Y;
                minX = MathF.Min(minX, x);
                maxX = MathF.Max(maxX, x);
                minY = MathF.Min(minY, y);
                maxY = MathF.Max(maxY, y);
            }

            Vector2 localCenter = new Vector2(0.5f * (minX + maxX), 0.5f * (minY + maxY));
            float cr = MathF.Cos(rotation);
            float sr = MathF.Sin(rotation);
            Vector2 center = mean + new Vector2(cr * localCenter.X - sr * localCenter.Y, sr * localCenter.X + cr * localCenter.Y);
            float size = MathF.Max(maxX - minX, maxY - minY);
            center = Shift(center, size, rotation, LandmarkShiftY);
            return new HandRoi(center, size * LandmarkScale, rotation);
        }
    }
}
