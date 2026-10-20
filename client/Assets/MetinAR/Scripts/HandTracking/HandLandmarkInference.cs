using System;
using Unity.InferenceEngine;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// On-device hand-landmark inference with Unity Sentis (com.unity.ai.inference): MediaPipe palm detector
    /// (192×192) then the 21-point hand landmarker (224×224) on a rotated crop. Input is an RGBA32 camera image
    /// (row 0 = top) in memory; nothing leaves the device. Pre/post-processing is the pure code in this folder.
    /// </summary>
    public sealed class HandLandmarkInference : IDisposable
    {
        public const int DetectorSize = PalmAnchors.DetectorInputSize;
        public const int LandmarkSize = HandLandmarkDecoder.LandmarkInputSize;

        private readonly Worker _detector;
        private readonly Worker _landmarker;
        private readonly NVector2[] _anchors;
        private readonly float[] _detectorBuffer = new float[DetectorSize * DetectorSize * 3];
        private readonly float[] _landmarkBuffer = new float[LandmarkSize * LandmarkSize * 3];
        private readonly Tensor<float> _detectorInput;
        private readonly Tensor<float> _landmarkInput;
        private bool _disposed;

        public HandLandmarkInference(ModelAsset palmDetector, ModelAsset handLandmarker, BackendType backend)
        {
            if (palmDetector == null || handLandmarker == null)
            {
                throw new ArgumentNullException(palmDetector == null ? nameof(palmDetector) : nameof(handLandmarker));
            }

            Backend = backend;
            _anchors = PalmAnchors.Generate();
            _detector = new Worker(ModelLoader.Load(palmDetector), backend);
            _landmarker = new Worker(ModelLoader.Load(handLandmarker), backend);
            _detectorInput = new Tensor<float>(new TensorShape(1, DetectorSize, DetectorSize, 3), _detectorBuffer);
            _landmarkInput = new Tensor<float>(new TensorShape(1, LandmarkSize, LandmarkSize, 3), _landmarkBuffer);
        }

        public BackendType Backend { get; }

        public float PalmScoreThreshold { get; set; } = 0.5f;

        /// <summary>Best palm score of the last detector run (also when below threshold).</summary>
        public float LastPalmScore { get; private set; }

        public static BackendType PreferredBackend(bool preferGpu)
        {
            return preferGpu && SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
        }

        // ------------------------------------------------------------------ async (runtime)

        public async Awaitable<(bool found, HandRoi roi)> DetectPalmAsync(byte[] rgba, int width, int height, Affine2D tensorToImage)
        {
            ScheduleDetector(rgba, width, height, tensorToImage);
            Awaitable<Tensor<float>> boxesWait = Output(_detector, "Identity").ReadbackAndCloneAsync();
            Awaitable<Tensor<float>> scoresWait = Output(_detector, "Identity_1").ReadbackAndCloneAsync();
            using Tensor<float> boxes = await boxesWait;
            using Tensor<float> scores = await scoresWait;
            return DecodePalm(boxes.DownloadToArray(), scores.DownloadToArray(), tensorToImage);
        }

        public async Awaitable<bool> LandmarksAsync(byte[] rgba, int width, int height, HandRoi roi, HandLandmarkFrame result)
        {
            ScheduleLandmarker(rgba, width, height, roi);
            Awaitable<Tensor<float>> lmWait = Output(_landmarker, "Identity").ReadbackAndCloneAsync();
            Awaitable<Tensor<float>> presenceWait = Output(_landmarker, "Identity_1").ReadbackAndCloneAsync();
            Awaitable<Tensor<float>> handedWait = Output(_landmarker, "Identity_2").ReadbackAndCloneAsync();
            Awaitable<Tensor<float>> worldWait = Output(_landmarker, "Identity_3").ReadbackAndCloneAsync();
            using Tensor<float> lm = await lmWait;
            using Tensor<float> presence = await presenceWait;
            using Tensor<float> handed = await handedWait;
            using Tensor<float> world = await worldWait;
            HandLandmarkDecoder.Decode(lm.DownloadToArray(), world.DownloadToArray(), presence.DownloadToArray()[0], handed.DownloadToArray()[0], roi, result);
            return true;
        }

        // ------------------------------------------------------------------ blocking (editor proof / tools)

        public (bool found, HandRoi roi) DetectPalm(byte[] rgba, int width, int height, Affine2D tensorToImage)
        {
            ScheduleDetector(rgba, width, height, tensorToImage);
            return DecodePalm(Output(_detector, "Identity").DownloadToArray(), Output(_detector, "Identity_1").DownloadToArray(), tensorToImage);
        }

        public void Landmarks(byte[] rgba, int width, int height, HandRoi roi, HandLandmarkFrame result)
        {
            ScheduleLandmarker(rgba, width, height, roi);
            HandLandmarkDecoder.Decode(
                Output(_landmarker, "Identity").DownloadToArray(),
                Output(_landmarker, "Identity_3").DownloadToArray(),
                Output(_landmarker, "Identity_1").DownloadToArray()[0],
                Output(_landmarker, "Identity_2").DownloadToArray()[0],
                roi,
                result);
        }

        // ------------------------------------------------------------------ shared

        private void ScheduleDetector(byte[] rgba, int width, int height, Affine2D tensorToImage)
        {
            ThrowIfDisposed();
            HandImageSampler.SampleRgb(rgba, width, height, width * 4, tensorToImage, DetectorSize, _detectorBuffer);
            _detectorInput.Upload(_detectorBuffer);
            _detector.Schedule(_detectorInput);
        }

        private void ScheduleLandmarker(byte[] rgba, int width, int height, HandRoi roi)
        {
            ThrowIfDisposed();
            HandImageSampler.SampleRgb(rgba, width, height, width * 4, roi.TensorToImage(LandmarkSize), LandmarkSize, _landmarkBuffer);
            _landmarkInput.Upload(_landmarkBuffer);
            _landmarker.Schedule(_landmarkInput);
        }

        private (bool, HandRoi) DecodePalm(float[] boxes, float[] scores, Affine2D tensorToImage)
        {
            bool found = PalmDetectionDecoder.TryDecodeBest(boxes, scores, _anchors, DetectorSize, PalmScoreThreshold, out PalmDetection detection);
            LastPalmScore = detection.Score;
            return found ? (true, HandRoiCalculator.FromPalm(detection, tensorToImage)) : (false, default);
        }

        private static Tensor<float> Output(Worker worker, string name)
        {
            if (!(worker.PeekOutput(name) is Tensor<float> tensor))
            {
                throw new InvalidOperationException("Model output missing: " + name);
            }

            return tensor;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HandLandmarkInference));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _detector?.Dispose();
            _landmarker?.Dispose();
            _detectorInput?.Dispose();
            _landmarkInput?.Dispose();
        }
    }
}
