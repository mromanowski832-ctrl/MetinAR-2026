using System;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// Maps between AR camera CPU-image pixels (sensor orientation, row 0 = top) and Unity viewport coordinates
    /// (0..1, origin bottom-left) using AR Foundation's display matrix — the same matrix the ARCore background shader
    /// applies: <c>tex = (u, v, 1, 0) · M</c> (row-vector convention), i.e. s = u·m00 + v·m10 + m20, t = u·m01 + v·m11 + m21.
    /// <see cref="FlipImageRows"/> selects whether texture t grows with the image row (default) or against it; the
    /// on-device mapping probe can pick the hypothesis that matches the rendered camera background.
    /// </summary>
    public sealed class CameraImageViewportMapper
    {
        private readonly Affine2D _viewportToImage;
        private readonly Affine2D _imageToViewport;

        public CameraImageViewportMapper(
            float m00, float m01, float m10, float m11, float m20, float m21,
            int imageWidth,
            int imageHeight,
            bool flipImageRows = false)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageWidth));
            }

            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            FlipImageRows = flipImageRows;
            var viewportToTexture = new Affine2D(m00, m10, m20, m01, m11, m21);
            Affine2D textureToImage = flipImageRows
                ? new Affine2D(imageWidth, 0f, 0f, 0f, -imageHeight, imageHeight)
                : Affine2D.Scale(imageWidth, imageHeight);
            _viewportToImage = textureToImage * viewportToTexture;
            _imageToViewport = _viewportToImage.Inverse();
        }

        /// <summary>Identity display matrix (image shown as-is, viewport y up ⇒ image row 0 at the top).</summary>
        public static CameraImageViewportMapper Identity(int imageWidth, int imageHeight)
        {
            // s = u, t = 1 − v  (texture t grows downward with the image rows).
            return new CameraImageViewportMapper(1f, 0f, 0f, -1f, 0f, 1f, imageWidth, imageHeight);
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public bool FlipImageRows { get; }

        public Affine2D ViewportToImage => _viewportToImage;

        public Affine2D ImageToViewportTransform => _imageToViewport;

        public Vector2 ImageToViewport(Vector2 imagePx) => _imageToViewport.Apply(imagePx);

        public Vector2 ViewportToImagePx(Vector2 viewport) => _viewportToImage.Apply(viewport);

        /// <summary>
        /// Detector input transform: tensor pixels (n×n, y down) → image pixels, letterboxing the whole camera image and
        /// rotating/mirroring it in 90° steps so the tensor shows the scene upright, as on the screen.
        /// </summary>
        public Affine2D UprightLetterbox(int tensorSize)
        {
            Vector2 right = SnapToAxis(_viewportToImage.ApplyVector(new Vector2(1f, 0f)));
            Vector2 down = SnapToAxis(_viewportToImage.ApplyVector(new Vector2(0f, -1f)));
            if (MathF.Abs(Vector2.Dot(right, down)) > 0.5f)
            {
                // Degenerate (should not happen with a real display matrix): fall back to the raw image.
                right = new Vector2(1f, 0f);
                down = new Vector2(0f, 1f);
            }

            float uprightWidth = MathF.Abs(right.X) > 0.5f ? ImageWidth : ImageHeight;
            float uprightHeight = MathF.Abs(down.Y) > 0.5f ? ImageHeight : ImageWidth;
            float s = MathF.Max(uprightWidth, uprightHeight) / tensorSize;
            Vector2 imageCenter = new Vector2(0.5f * ImageWidth, 0.5f * ImageHeight);
            Vector2 origin = imageCenter - 0.5f * tensorSize * s * (right + down);
            return new Affine2D(s * right.X, s * down.X, origin.X, s * right.Y, s * down.Y, origin.Y);
        }

        private static Vector2 SnapToAxis(Vector2 v)
        {
            if (MathF.Abs(v.X) >= MathF.Abs(v.Y))
            {
                return new Vector2(MathF.Sign(v.X) == 0 ? 1f : MathF.Sign(v.X), 0f);
            }

            return new Vector2(0f, MathF.Sign(v.Y));
        }
    }
}
