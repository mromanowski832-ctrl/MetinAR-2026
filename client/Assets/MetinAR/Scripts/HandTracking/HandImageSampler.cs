using System;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// Fills an NHWC float tensor buffer (RGB, 0..1) by bilinear sampling an RGBA32 image (row 0 = top) through an
    /// affine transform from tensor pixels to image pixels. Pixels outside the image are black (letterbox / crop border).
    /// Pure C# so the crop geometry is unit-tested; runs on the main thread for 192² and 224² tensors.
    /// </summary>
    public static class HandImageSampler
    {
        private const float Inv255 = 1f / 255f;

        public static void SampleRgb(
            byte[] rgba,
            int width,
            int height,
            int rowStride,
            Affine2D tensorToImage,
            int tensorSize,
            float[] destination)
        {
            if (rgba == null)
            {
                throw new ArgumentNullException(nameof(rgba));
            }

            if (destination == null || destination.Length < tensorSize * tensorSize * 3)
            {
                throw new ArgumentException("Destination too small.", nameof(destination));
            }

            if (rowStride < width * 4 || rgba.Length < rowStride * (height - 1) + width * 4)
            {
                throw new ArgumentException("Image buffer too small.", nameof(rgba));
            }

            int o = 0;
            for (int ty = 0; ty < tensorSize; ty++)
            {
                float cy = ty + 0.5f;
                for (int tx = 0; tx < tensorSize; tx++)
                {
                    float cx = tx + 0.5f;
                    float x = tensorToImage.A * cx + tensorToImage.B * cy + tensorToImage.C - 0.5f;
                    float y = tensorToImage.D * cx + tensorToImage.E * cy + tensorToImage.F - 0.5f;
                    int x0 = (int)MathF.Floor(x);
                    int y0 = (int)MathF.Floor(y);
                    float fx = x - x0;
                    float fy = y - y0;
                    float r = 0f, g = 0f, b = 0f;
                    Accumulate(rgba, width, height, rowStride, x0, y0, (1f - fx) * (1f - fy), ref r, ref g, ref b);
                    Accumulate(rgba, width, height, rowStride, x0 + 1, y0, fx * (1f - fy), ref r, ref g, ref b);
                    Accumulate(rgba, width, height, rowStride, x0, y0 + 1, (1f - fx) * fy, ref r, ref g, ref b);
                    Accumulate(rgba, width, height, rowStride, x0 + 1, y0 + 1, fx * fy, ref r, ref g, ref b);
                    destination[o++] = r * Inv255;
                    destination[o++] = g * Inv255;
                    destination[o++] = b * Inv255;
                }
            }
        }

        private static void Accumulate(byte[] rgba, int width, int height, int rowStride, int x, int y, float w, ref float r, ref float g, ref float b)
        {
            if (w <= 0f || x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            int i = y * rowStride + x * 4;
            r += rgba[i] * w;
            g += rgba[i + 1] * w;
            b += rgba[i + 2] * w;
        }
    }
}
