using System;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>One palm found by the palm detector (coordinates in detector-tensor pixels, y down).</summary>
    public struct PalmDetection
    {
        public const int KeypointCount = 7;

        /// <summary>Sigmoid confidence 0..1.</summary>
        public float Score;

        public int AnchorIndex;

        public Vector2 BoxCenter;

        public float BoxWidth;

        public float BoxHeight;

        /// <summary>Palm keypoints: 0 = wrist centre, 2 = middle-finger MCP (used for the hand rotation).</summary>
        public Vector2[] Keypoints;
    }

    /// <summary>
    /// Decodes the raw palm-detector output: boxes (anchors × 18: dx, dy, w, h, 7 × (kx, ky)) relative to each anchor
    /// in tensor pixels, and one raw logit per anchor. Single-hand prototype: the best-scoring anchor wins.
    /// </summary>
    public static class PalmDetectionDecoder
    {
        public const int ValuesPerAnchor = 18;

        public static float Sigmoid(float logit)
        {
            float x = Math.Clamp(logit, -100f, 100f);
            return 1f / (1f + MathF.Exp(-x));
        }

        /// <returns>True when the best anchor's score is at least <paramref name="scoreThreshold"/>.</returns>
        public static bool TryDecodeBest(
            ReadOnlySpan<float> rawBoxes,
            ReadOnlySpan<float> rawScores,
            Vector2[] anchors,
            int inputSize,
            float scoreThreshold,
            out PalmDetection detection)
        {
            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            int count = anchors.Length;
            if (rawScores.Length < count || rawBoxes.Length < count * ValuesPerAnchor)
            {
                throw new ArgumentException("Detector output smaller than the anchor table.");
            }

            int best = -1;
            float bestLogit = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float logit = rawScores[i];
                if (!float.IsNaN(logit) && logit > bestLogit)
                {
                    bestLogit = logit;
                    best = i;
                }
            }

            detection = default;
            if (best < 0)
            {
                return false;
            }

            float score = Sigmoid(bestLogit);
            Vector2 anchor = anchors[best] * inputSize;
            int o = best * ValuesPerAnchor;
            detection.Score = score;
            detection.AnchorIndex = best;
            detection.BoxCenter = anchor + new Vector2(rawBoxes[o], rawBoxes[o + 1]);
            detection.BoxWidth = rawBoxes[o + 2];
            detection.BoxHeight = rawBoxes[o + 3];
            detection.Keypoints = new Vector2[PalmDetection.KeypointCount];
            for (int k = 0; k < PalmDetection.KeypointCount; k++)
            {
                detection.Keypoints[k] = anchor + new Vector2(rawBoxes[o + 4 + 2 * k], rawBoxes[o + 5 + 2 * k]);
            }

            return score >= scoreThreshold && detection.BoxWidth > 0f && detection.BoxHeight > 0f;
        }
    }
}
