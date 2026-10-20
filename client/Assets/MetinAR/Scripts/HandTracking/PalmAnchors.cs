using System;
using System.Collections.Generic;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// SSD anchor centres for the MediaPipe palm detector (written from the published model description, not copied):
    /// 192 px input, feature strides 8/16/16/16, two anchors per layer and cell, fixed anchor size.
    /// Consecutive layers with the same stride share one grid, so the result is 24×24×2 + 12×12×6 = 2016 anchors,
    /// ordered grid by grid, row by row, column by column, anchor by anchor.
    /// </summary>
    public static class PalmAnchors
    {
        public const int DetectorInputSize = 192;
        public const int ExpectedCount = 2016;

        private static readonly int[] DefaultStrides = { 8, 16, 16, 16 };
        private const int AnchorsPerLayerAndCell = 2;

        /// <summary>Normalised anchor centres (0..1, y down, same orientation as the detector input tensor).</summary>
        public static Vector2[] Generate(int inputSize = DetectorInputSize, IReadOnlyList<int> strides = null)
        {
            strides ??= DefaultStrides;
            var result = new List<Vector2>();
            int layer = 0;
            while (layer < strides.Count)
            {
                int stride = strides[layer];
                int sameStrideLayers = 0;
                while (layer + sameStrideLayers < strides.Count && strides[layer + sameStrideLayers] == stride)
                {
                    sameStrideLayers++;
                }

                int perCell = sameStrideLayers * AnchorsPerLayerAndCell;
                int rows = (int)Math.Ceiling(inputSize / (double)stride);
                int cols = rows;
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        var centre = new Vector2((x + 0.5f) / cols, (y + 0.5f) / rows);
                        for (int k = 0; k < perCell; k++)
                        {
                            result.Add(centre);
                        }
                    }
                }

                layer += sameStrideLayers;
            }

            return result.ToArray();
        }
    }
}
