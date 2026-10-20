using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MetinAR.HandTracking;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using NVector2 = System.Numerics.Vector2;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-007 milestone 2: isolated camera-frame → landmarks proof with the real models and Sentis, using the
    /// same code the AR tracker runs (image downsampled like the camera path, upright letterbox, palm detector, ROI,
    /// landmarker). Optionally compares the landmarks with an independent onnxruntime reference
    /// (tools/hand_tracking_reference.py).
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.HandTrackingInferenceProof.Run
    ///        -handImage photo.jpg [-handReference ref.json] [-handBackend CPU|GPUCompute] [-handMaxDim 640]
    /// Writes Builds/metinar-007-hand-proof-&lt;backend&gt;.json (+ .png overlay) and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class HandTrackingInferenceProof
    {
        public const string PalmModelPath = "Assets/MetinAR/ThirdParty/BlazeHand/hand_detector.onnx";
        public const string LandmarkModelPath = "Assets/MetinAR/ThirdParty/BlazeHand/hand_landmarks_detector.onnx";

        public static void Run()
        {
            int exitCode = 1;
            try
            {
                exitCode = Execute() ? 0 : 1;
            }
            catch (Exception ex)
            {
                Debug.LogError("[HandProof] FAILED with exception: " + ex);
            }

            EditorApplication.Exit(exitCode);
        }

        private static string Arg(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        private static bool Execute()
        {
            string imagePath = Arg("-handImage", null);
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                Debug.LogError("[HandProof] -handImage missing or not found: " + imagePath);
                return false;
            }

            BackendType backend = (BackendType)Enum.Parse(typeof(BackendType), Arg("-handBackend", "CPU"));
            int maxDim = int.Parse(Arg("-handMaxDim", "640"), CultureInfo.InvariantCulture);
            string referencePath = Arg("-handReference", null);

            var palm = AssetDatabase.LoadAssetAtPath<ModelAsset>(PalmModelPath);
            var landmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkModelPath);
            if (palm == null || landmarker == null)
            {
                Debug.LogError("[HandProof] model assets not imported");
                return false;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(imagePath));
            int divisor = 1;
            while (Mathf.Max(source.width, source.height) / divisor > maxDim)
            {
                divisor *= 2;
            }

            int width = source.width / divisor;
            int height = source.height / divisor;
            byte[] rgba = DownsampleTopDown(source, divisor, width, height);

            var mapper = CameraImageViewportMapper.Identity(width, height);
            var frame = new HandLandmarkFrame();
            bool found;
            HandRoi roi;
            float palmScore;
            double firstMs;
            double steadyMs;
            using (var inference = new HandLandmarkInference(palm, landmarker, backend))
            {
                var watch = Stopwatch.StartNew();
                (found, roi) = inference.DetectPalm(rgba, width, height, mapper.UprightLetterbox(HandLandmarkInference.DetectorSize));
                palmScore = inference.LastPalmScore;
                if (found)
                {
                    inference.Landmarks(rgba, width, height, roi, frame);
                }

                firstMs = watch.Elapsed.TotalMilliseconds;
                const int runs = 10;
                watch.Restart();
                for (int i = 0; i < runs && found; i++)
                {
                    inference.DetectPalm(rgba, width, height, mapper.UprightLetterbox(HandLandmarkInference.DetectorSize));
                    inference.Landmarks(rgba, width, height, roi, new HandLandmarkFrame());
                }

                steadyMs = found ? watch.Elapsed.TotalMilliseconds / runs : 0;
            }

            float scale = divisor; // working image px → source image px
            var landmarksSource = new NVector2[HandLandmark.Count];
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                landmarksSource[i] = frame.ImagePoint2D(i) * scale;
            }

            double meanError = -1;
            double maxError = -1;
            double referenceRoi = 0;
            if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath) && found)
            {
                string json = File.ReadAllText(referencePath);
                List<NVector2> reference = ParseLandmarks(json);
                referenceRoi = ParseNumber(json, "\"size\"");
                if (reference.Count == HandLandmark.Count && referenceRoi > 0)
                {
                    double sum = 0;
                    maxError = 0;
                    for (int i = 0; i < HandLandmark.Count; i++)
                    {
                        double e = NVector2.Distance(reference[i], landmarksSource[i]) / referenceRoi;
                        sum += e;
                        maxError = Math.Max(maxError, e);
                    }

                    meanError = sum / HandLandmark.Count;
                }
            }

            bool pass = found && palmScore >= 0.5f && frame.Presence >= 0.5f
                        && (meanError < 0 || (meanError < 0.03 && maxError < 0.08))
                        && (string.IsNullOrEmpty(referencePath) || meanError >= 0);

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string builds = Path.Combine(Directory.GetParent(projectRoot)!.FullName, "Builds");
            Directory.CreateDirectory(builds);
            string stem = Path.Combine(builds, "metinar-007-hand-proof-" + backend);
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\n");
            sb.Append("  \"pass\": ").Append(pass ? "true" : "false").Append(",\n");
            sb.Append("  \"backend\": \"").Append(backend).Append("\",\n");
            sb.Append("  \"sourceImage\": {\"width\": ").Append(source.width).Append(", \"height\": ").Append(source.height).Append("},\n");
            sb.Append("  \"workingImage\": {\"width\": ").Append(width).Append(", \"height\": ").Append(height).Append("},\n");
            sb.Append("  \"palmFound\": ").Append(found ? "true" : "false").Append(",\n");
            sb.Append("  \"palmScore\": ").Append(palmScore.ToString("0.0000", ci)).Append(",\n");
            sb.Append("  \"presence\": ").Append(frame.Presence.ToString("0.0000", ci)).Append(",\n");
            sb.Append("  \"handedness\": ").Append(frame.Handedness.ToString("0.0000", ci)).Append(",\n");
            sb.Append("  \"roi\": {\"centerX\": ").Append((roi.Center.X * scale).ToString("0.0", ci)).Append(", \"centerY\": ")
              .Append((roi.Center.Y * scale).ToString("0.0", ci)).Append(", \"size\": ").Append((roi.Size * scale).ToString("0.0", ci))
              .Append(", \"rotationDeg\": ").Append((roi.Rotation * 180.0 / Math.PI).ToString("0.00", ci)).Append("},\n");
            sb.Append("  \"firstRunMs\": ").Append(firstMs.ToString("0.0", ci)).Append(",\n");
            sb.Append("  \"steadyPalmPlusLandmarksMs\": ").Append(steadyMs.ToString("0.0", ci)).Append(",\n");
            sb.Append("  \"referenceMeanErrorOfRoi\": ").Append(meanError.ToString("0.0000", ci)).Append(",\n");
            sb.Append("  \"referenceMaxErrorOfRoi\": ").Append(maxError.ToString("0.0000", ci)).Append(",\n");
            sb.Append("  \"landmarksPx\": [");
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                sb.Append(i == 0 ? "" : ", ").Append('[').Append(landmarksSource[i].X.ToString("0.0", ci)).Append(", ")
                  .Append(landmarksSource[i].Y.ToString("0.0", ci)).Append(']');
            }

            sb.Append("]\n}\n");
            File.WriteAllText(stem + ".json", sb.ToString());
            WriteOverlay(rgba, width, height, frame, found, stem + ".png");
            Debug.Log("[HandProof] " + (pass ? "PASS" : "FAIL") + " backend=" + backend + " palm=" + palmScore.ToString("0.000", ci)
                      + " presence=" + frame.Presence.ToString("0.000", ci) + " meanErr=" + meanError.ToString("0.0000", ci)
                      + " maxErr=" + maxError.ToString("0.0000", ci) + " steadyMs=" + steadyMs.ToString("0.0", ci) + " -> " + stem + ".json");
            UnityEngine.Object.DestroyImmediate(source);
            return pass;
        }

        /// <summary>Box-filter downsample into RGBA32 with row 0 = top (the AR camera CPU image layout).</summary>
        private static byte[] DownsampleTopDown(Texture2D source, int divisor, int width, int height)
        {
            Color32[] pixels = source.GetPixels32(); // row 0 = bottom
            var rgba = new byte[width * height * 4];
            int n = divisor * divisor;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < divisor; dy++)
                    {
                        int sy = source.height - 1 - (y * divisor + dy);
                        for (int dx = 0; dx < divisor; dx++)
                        {
                            Color32 c = pixels[sy * source.width + x * divisor + dx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                        }
                    }

                    int o = (y * width + x) * 4;
                    rgba[o] = (byte)(r / n);
                    rgba[o + 1] = (byte)(g / n);
                    rgba[o + 2] = (byte)(b / n);
                    rgba[o + 3] = 255;
                }
            }

            return rgba;
        }

        private static void WriteOverlay(byte[] rgba, int width, int height, HandLandmarkFrame frame, bool found, string path)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    pixels[(height - 1 - y) * width + x] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], 255);
                }
            }

            if (found)
            {
                for (int i = 0; i < HandLandmark.Count; i++)
                {
                    NVector2 p = frame.ImagePoint2D(i);
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int x = (int)p.X + dx;
                            int y = (int)p.Y + dy;
                            if (x >= 0 && x < width && y >= 0 && y < height)
                            {
                                pixels[(height - 1 - y) * width + x] = i == 0 ? new Color32(255, 220, 0, 255) : new Color32(255, 40, 40, 255);
                            }
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static List<NVector2> ParseLandmarks(string json)
        {
            var result = new List<NVector2>();
            int start = json.IndexOf("\"landmarksPx\"", StringComparison.Ordinal);
            if (start < 0)
            {
                return result;
            }

            foreach (Match m in Regex.Matches(json.Substring(start), @"\[\s*(-?[0-9.eE+-]+)\s*,\s*(-?[0-9.eE+-]+)\s*\]"))
            {
                result.Add(new NVector2(
                    float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
            }

            return result;
        }

        private static double ParseNumber(string json, string key)
        {
            Match m = Regex.Match(json, Regex.Escape(key) + @"\s*:\s*(-?[0-9.eE+-]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
