using System;
using System.Collections;
using System.Diagnostics;
using MetinAR.ArEncounter;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Debug = UnityEngine.Debug;
using NVector2 = System.Numerics.Vector2;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// METINAR-007: reads the live AR camera image from AR Foundation (CPU image, no second camera user), runs the
    /// on-device hand-landmark models and publishes one <see cref="HandObservation"/> per inference. Runs only while the
    /// encounter is scanning / ready / placed. Model or inference failures never break the AR encounter: tracking just
    /// reports itself unavailable. Camera frames stay in memory and are discarded after inference.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class ArHandTracker : MonoBehaviour
    {
        [SerializeField]
        private ModelAsset _palmDetectorModel;

        [SerializeField]
        private ModelAsset _handLandmarkModel;

        [SerializeField]
        private ARCameraManager _cameraManager;

        [SerializeField]
        private Camera _arCamera;

        [SerializeField]
        private ArEncounterSceneController _encounter;

        [SerializeField]
        private float _palmScoreThreshold = 0.5f;

        [SerializeField]
        private float _presenceThreshold = 0.5f;

        [SerializeField]
        private int _maxImageDimension = 640;

        [SerializeField]
        private bool _preferGpu = true;

        [SerializeField]
        [Tooltip("Allow Sentis GPUCompute on OpenGL ES devices. Off by default: on GLES (e.g. Mali A31) GPUCompute failed with a TypeInitializationException, so CPU is used.")]
        private bool _allowGpuOnOpenGles;

        [SerializeField]
        private bool _runMappingProbe = true;

        private readonly HandObservation _latest = new HandObservation();
        private readonly HandObservation _scratch = new HandObservation();
        private readonly HandLandmarkFrame _frame = new HandLandmarkFrame();
        private HandLandmarkInference _inference;
        private NativeArray<byte> _nativeBuffer;
        private byte[] _rgba;
        private Matrix4x4? _displayMatrix;
        private HandRoi? _trackedRoi;
        private bool _busy;
        private bool _disposeRequested;
        private int _sequence;
        private bool _flipImageRows;
        private bool _probeStarted;
        private HandInferenceBackendPolicy _backendPolicy;
        private int _failureCount;

        /// <summary>Raised on the main thread for every published observation (with or without a hand).</summary>
        public event Action<HandObservation> ObservationUpdated;

        /// <summary>
        /// Test seam: when set, it fills the observation each frame instead of the camera + model path
        /// (same downstream path: sword pose, smoothing, hit gating).
        /// </summary>
        public Action<HandObservation> ObservationOverride { get; set; }

        public HandObservation Latest => _latest;

        public bool ModelsLoaded => _inference != null;

        public string BackendName => _inference != null ? _inference.Backend.ToString() : "none";

        /// <summary>True when GPU inference failed this session and the tracker fell back to CPU.</summary>
        public bool GpuFallbackUsed => _backendPolicy != null && _backendPolicy.GpuFailed;

        /// <summary>Last model/inference error (null when healthy).</summary>
        public string LastError { get; private set; }

        public int CpuImageCount { get; private set; }

        public int InferenceCount { get; private set; }

        public int PalmDetectorRuns { get; private set; }

        public int HandFrames { get; private set; }

        public float LastInferenceMilliseconds { get; private set; }

        public Vector2Int LastImageSize { get; private set; }

        public bool HasDisplayMatrix => _displayMatrix.HasValue;

        /// <summary>Mapping hypothesis in use (see <see cref="CameraImageViewportMapper.FlipImageRows"/>).</summary>
        public bool FlipImageRows => _flipImageRows;

        /// <summary>"pending", "inconclusive", or the probe scores; set once per session by the mapping probe.</summary>
        public string MappingProbeResult { get; private set; } = "pending";

        public bool IsTrackingActive
        {
            get
            {
                if (_encounter == null)
                {
                    return true;
                }

                ArEncounterState s = _encounter.State;
                return !_encounter.IsExiting
                       && (s == ArEncounterState.Scanning || s == ArEncounterState.ReadyToPlace || s == ArEncounterState.Placed);
            }
        }

        public Camera ArCamera => _arCamera;

        public void Configure(ModelAsset palmDetector, ModelAsset handLandmarker, ARCameraManager cameraManager, Camera arCamera, ArEncounterSceneController encounter)
        {
            _palmDetectorModel = palmDetector;
            _handLandmarkModel = handLandmarker;
            _cameraManager = cameraManager;
            _arCamera = arCamera;
            _encounter = encounter;
        }

        public ModelAsset PalmDetectorModel => _palmDetectorModel;

        public ModelAsset HandLandmarkModel => _handLandmarkModel;

        private void OnEnable()
        {
            if (_cameraManager != null)
            {
                _cameraManager.frameReceived += OnCameraFrame;
            }
        }

        private void OnDisable()
        {
            if (_cameraManager != null)
            {
                _cameraManager.frameReceived -= OnCameraFrame;
            }
        }

        private void Start()
        {
            _backendPolicy = new HandInferenceBackendPolicy(_preferGpu, _allowGpuOnOpenGles);
            bool isGles = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
            HandInferenceBackend choice = _backendPolicy.Choose(SystemInfo.supportsComputeShaders, isGles);
            Debug.Log("[HandTracking] backend choice=" + choice + " preferGpu=" + _preferGpu + " allowGpuOnGles=" + _allowGpuOnOpenGles
                      + " graphics=" + SystemInfo.graphicsDeviceType + " computeShaders=" + SystemInfo.supportsComputeShaders);
            if (!TryCreateInference(choice) && choice == HandInferenceBackend.GpuCompute && _backendPolicy.ReportFailure(choice))
            {
                TryCreateInference(HandInferenceBackend.Cpu);
            }
        }

        private bool TryCreateInference(HandInferenceBackend backend)
        {
            try
            {
                BackendType type = backend == HandInferenceBackend.GpuCompute ? BackendType.GPUCompute : BackendType.CPU;
                _inference = new HandLandmarkInference(_palmDetectorModel, _handLandmarkModel, type)
                {
                    PalmScoreThreshold = _palmScoreThreshold,
                };
                Debug.Log("[HandTracking] models loaded, backend=" + _inference.Backend + " computeShaders=" + SystemInfo.supportsComputeShaders);
                return true;
            }
            catch (Exception ex)
            {
                _inference = null;
                LastError = "model load failed (" + backend + "): " + HandInferenceBackendPolicy.DescribeChain(ex);
                Debug.LogWarning("[HandTracking] " + LastError + "\n" + ex);
                return false;
            }
        }

        private void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue)
            {
                _displayMatrix = args.displayMatrix.Value;
            }
        }

        private void Update()
        {
            if (ObservationOverride != null)
            {
                _scratch.HasHand = false;
                _scratch.Presence = 0f;
                _scratch.FromTracking = false;
                ObservationOverride(_scratch);
                _scratch.Source = "override";
                CaptureCameraPose(_scratch);
                Publish(_scratch);
                return;
            }

            if (_busy || _inference == null || !IsTrackingActive || _cameraManager == null || !_cameraManager.enabled)
            {
                return;
            }

            if (_runMappingProbe && !_probeStarted && _displayMatrix.HasValue && _encounter != null && Time.timeSinceLevelLoad > 3f)
            {
                _probeStarted = true;
                StartCoroutine(MappingProbe());
            }

            if (!TryCopyLatestImage(out int width, out int height))
            {
                return;
            }

            _scratch.Source = "camera";
            CaptureCameraPose(_scratch);
            CameraImageViewportMapper mapper = BuildMapper(width, height, _flipImageRows);
            _busy = true;
            _ = RunAsync(width, height, mapper);
        }

        private void CaptureCameraPose(HandObservation o)
        {
            if (_arCamera != null)
            {
                o.CameraPosition = _arCamera.transform.position;
                o.CameraRotation = _arCamera.transform.rotation;
                o.CameraProjection = _arCamera.projectionMatrix;
            }
        }

        private CameraImageViewportMapper BuildMapper(int width, int height, bool flipRows)
        {
            if (!_displayMatrix.HasValue)
            {
                return CameraImageViewportMapper.Identity(width, height);
            }

            Matrix4x4 m = _displayMatrix.Value;
            try
            {
                return new CameraImageViewportMapper(m.m00, m.m01, m.m10, m.m11, m.m20, m.m21, width, height, flipRows);
            }
            catch (InvalidOperationException)
            {
                return CameraImageViewportMapper.Identity(width, height);
            }
        }

        private bool TryCopyLatestImage(out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!_cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                return false;
            }

            using (image)
            {
                int max = Mathf.Max(image.width, image.height);
                int divisor = 1;
                while (max / divisor > _maxImageDimension)
                {
                    divisor *= 2;
                }

                width = image.width / divisor;
                height = image.height / divisor;
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
                {
                    outputDimensions = new Vector2Int(width, height),
                };
                int size = image.GetConvertedDataSize(conversion);
                if (!_nativeBuffer.IsCreated || _nativeBuffer.Length != size)
                {
                    if (_nativeBuffer.IsCreated)
                    {
                        _nativeBuffer.Dispose();
                    }

                    _nativeBuffer = new NativeArray<byte>(size, Allocator.Persistent);
                    _rgba = new byte[size];
                }

                image.Convert(conversion, _nativeBuffer);
            }

            _nativeBuffer.CopyTo(_rgba);
            CpuImageCount++;
            LastImageSize = new Vector2Int(width, height);
            return true;
        }

        private async Awaitable RunAsync(int width, int height, CameraImageViewportMapper mapper)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                HandRoi roi;
                bool fromTracking = _trackedRoi.HasValue;
                float palmScore = 0f;
                if (fromTracking)
                {
                    roi = _trackedRoi.Value;
                }
                else
                {
                    PalmDetectorRuns++;
                    (bool found, HandRoi palmRoi) = await _inference.DetectPalmAsync(_rgba, width, height, mapper.UprightLetterbox(HandLandmarkInference.DetectorSize));
                    palmScore = _inference.LastPalmScore;
                    if (_disposeRequested)
                    {
                        return;
                    }

                    if (!found || !palmRoi.IsValid)
                    {
                        FinishNoHand(watch, palmScore);
                        return;
                    }

                    roi = palmRoi;
                }

                await _inference.LandmarksAsync(_rgba, width, height, roi, _frame);
                if (_disposeRequested)
                {
                    return;
                }

                if (_frame.Presence < _presenceThreshold)
                {
                    _trackedRoi = null;
                    FinishNoHand(watch, palmScore);
                    return;
                }

                var imagePoints = new NVector2[HandLandmark.Count];
                for (int i = 0; i < HandLandmark.Count; i++)
                {
                    imagePoints[i] = _frame.ImagePoint2D(i);
                    _scratch.Viewport[i] = mapper.ImageToViewport(imagePoints[i]);
                    _scratch.World[i] = _frame.WorldPoints[i];
                }

                HandRoi next = HandRoiCalculator.FromLandmarks(imagePoints);
                _trackedRoi = next.IsValid ? next : (HandRoi?)null;
                _scratch.HasHand = true;
                _scratch.Presence = _frame.Presence;
                _scratch.Handedness = _frame.Handedness;
                _scratch.PalmScore = palmScore;
                _scratch.FromTracking = fromTracking;
                HandFrames++;
                Finish(watch);
            }
            catch (Exception ex)
            {
                _trackedRoi = null;
                HandleInferenceFailure(ex);
            }
            finally
            {
                _busy = false;
                if (_disposeRequested)
                {
                    DisposeInference();
                }
            }
        }

        private void HandleInferenceFailure(Exception ex)
        {
            _failureCount++;
            HandInferenceBackend failed = _inference != null && _inference.Backend == BackendType.GPUCompute
                ? HandInferenceBackend.GpuCompute
                : HandInferenceBackend.Cpu;
            LastError = "inference failed (" + failed + "): " + HandInferenceBackendPolicy.DescribeChain(ex);
            if (HandInferenceBackendPolicy.ShouldLogFailure(_failureCount))
            {
                // Full exception (inner chain + stack) only on the 1st and every 300th failure; never every frame.
                Debug.LogWarning("[HandTracking] " + LastError + " (failure #" + _failureCount + ")\n" + ex);
            }

            if (_disposeRequested || _backendPolicy == null || !_backendPolicy.ReportFailure(failed))
            {
                return;
            }

            // First GPU failure: drop the GPU workers and continue on CPU; GPU is not retried this session.
            Debug.LogWarning("[HandTracking] GPU inference failed, falling back to CPU backend");
            _inference?.Dispose();
            _inference = null;
            _failureCount = 0;
            TryCreateInference(HandInferenceBackend.Cpu);
        }

        private void FinishNoHand(Stopwatch watch, float palmScore)
        {
            _scratch.HasHand = false;
            _scratch.Presence = _frame.Presence;
            _scratch.PalmScore = palmScore;
            _scratch.FromTracking = false;
            Finish(watch);
        }

        private void Finish(Stopwatch watch)
        {
            InferenceCount++;
            LastInferenceMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            _scratch.InferenceMilliseconds = LastInferenceMilliseconds;
            LastError = null;
            Publish(_scratch);
            if (InferenceCount == 1 || InferenceCount % 150 == 0)
            {
                Debug.Log("[HandTracking] inference #" + InferenceCount + " hand=" + _scratch.HasHand + " presence=" + _scratch.Presence.ToString("0.00")
                          + " palm=" + _scratch.PalmScore.ToString("0.00") + " ms=" + LastInferenceMilliseconds.ToString("0") + " image=" + LastImageSize.x + "x" + LastImageSize.y
                          + " backend=" + BackendName + " handFrames=" + HandFrames + " detectorRuns=" + PalmDetectorRuns);
            }
        }

        private void Publish(HandObservation source)
        {
            source.Sequence = ++_sequence;
            source.Time = Time.time;
            _latest.CopyFrom(source);
            ObservationUpdated?.Invoke(_latest);
        }

        // ------------------------------------------------------------------ mapping probe

        /// <summary>
        /// One-shot on-device check of the image → screen mapping: compares the rendered camera background with the
        /// CPU image sampled through each mapping hypothesis (normalised cross-correlation of 32×32 grey samples in the
        /// screen centre). Only numbers are logged; no image is stored. A clear winner becomes the mapping in use.
        /// </summary>
        private IEnumerator MappingProbe()
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                yield return new WaitForEndOfFrame();
                if (_busy || _inference == null)
                {
                    continue;
                }

                _busy = true;
                try
                {
                    RunMappingProbeOnce();
                }
                catch (Exception ex)
                {
                    MappingProbeResult = "inconclusive (" + ex.GetType().Name + ")";
                    Debug.LogWarning("[HandTracking] mapping probe failed: " + ex.Message);
                }
                finally
                {
                    _busy = false;
                }

                yield break;
            }

            MappingProbeResult = "inconclusive (busy)";
        }

        private void RunMappingProbeOnce()
        {
            Texture2D screen = null;
            try
            {
                screen = ScreenCapture.CaptureScreenshotAsTexture();
                if (screen == null || !TryCopyLatestImage(out int width, out int height))
                {
                    MappingProbeResult = "inconclusive (no capture)";
                    return;
                }

                const int n = 32;
                var screenGrey = new float[n * n];
                Color32[] pixels = screen.GetPixels32();
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = Mathf.Lerp(0.2f, 0.8f, (x + 0.5f) / n);
                        float v = Mathf.Lerp(0.35f, 0.65f, (y + 0.5f) / n);
                        int px = Mathf.Clamp((int)(u * screen.width), 0, screen.width - 1);
                        int py = Mathf.Clamp((int)(v * screen.height), 0, screen.height - 1); // Texture2D row 0 = bottom = viewport v 0
                        Color32 c = pixels[py * screen.width + px];
                        screenGrey[y * n + x] = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    }
                }

                float noFlip = Correlate(screenGrey, n, BuildMapper(width, height, false), width, height, false);
                float flip = Correlate(screenGrey, n, BuildMapper(width, height, true), width, height, false);
                float mirrorControl = Correlate(screenGrey, n, BuildMapper(width, height, false), width, height, true);
                bool decisive = Mathf.Max(noFlip, flip) > 0.5f && Mathf.Abs(noFlip - flip) > 0.2f;
                if (decisive)
                {
                    _flipImageRows = flip > noFlip;
                    _trackedRoi = null;
                }

                MappingProbeResult = (decisive ? "selected flipRows=" + _flipImageRows : "inconclusive")
                                     + " ncc(noFlip)=" + noFlip.ToString("0.00") + " ncc(flipRows)=" + flip.ToString("0.00")
                                     + " ncc(mirrorControl)=" + mirrorControl.ToString("0.00");
                Debug.Log("[HandTracking] mapping probe " + MappingProbeResult + " image=" + width + "x" + height + " screen=" + screen.width + "x" + screen.height);
            }
            finally
            {
                if (screen != null)
                {
                    Destroy(screen);
                }
            }
        }

        private float Correlate(float[] screenGrey, int n, CameraImageViewportMapper mapper, int width, int height, bool mirrorX)
        {
            var sample = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = Mathf.Lerp(0.2f, 0.8f, (x + 0.5f) / n);
                    float v = Mathf.Lerp(0.35f, 0.65f, (y + 0.5f) / n);
                    NVector2 p = mapper.ViewportToImagePx(new NVector2(u, v));
                    int ix = Mathf.Clamp((int)p.X, 0, width - 1);
                    if (mirrorX)
                    {
                        ix = width - 1 - ix;
                    }

                    int iy = Mathf.Clamp((int)p.Y, 0, height - 1);
                    int i = (iy * width + ix) * 4;
                    sample[y * n + x] = 0.299f * _rgba[i] + 0.587f * _rgba[i + 1] + 0.114f * _rgba[i + 2];
                }
            }

            return Ncc(screenGrey, sample);
        }

        private static float Ncc(float[] a, float[] b)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                ma += a[i];
                mb += b[i];
            }

            ma /= a.Length;
            mb /= b.Length;
            double num = 0, da = 0, db = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double x = a[i] - ma;
                double y = b[i] - mb;
                num += x * y;
                da += x * x;
                db += y * y;
            }

            return da < 1e-6 || db < 1e-6 ? 0f : (float)(num / Math.Sqrt(da * db));
        }

        // ------------------------------------------------------------------ teardown

        private void OnDestroy()
        {
            if (_busy)
            {
                _disposeRequested = true;
            }
            else
            {
                DisposeInference();
            }
        }

        private void DisposeInference()
        {
            _inference?.Dispose();
            _inference = null;
            if (_nativeBuffer.IsCreated)
            {
                _nativeBuffer.Dispose();
            }
        }
    }
}
