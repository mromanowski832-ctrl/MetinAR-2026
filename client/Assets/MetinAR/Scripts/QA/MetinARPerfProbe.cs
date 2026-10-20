using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MetinAR.QA
{
    /// <summary>
    /// METINAR-011 passive performance probe. Active ONLY in Android/iOS development players (never in the Editor, never in
    /// release builds). Every <see cref="WindowSeconds"/> it writes one <c>[MetinARPerf]</c> line to the player log
    /// (logcat on Android) with frame time, main-thread time, spike counts, memory and any latency samples reported via
    /// <see cref="RecordLatency"/> (e.g. hand-tracking inference once METINAR-007 exposes it). It does not change gameplay,
    /// rendering, scenes or input. tools/qa/Invoke-MetinARDeviceSmoke.ps1 parses the lines and checks prototype budgets.
    /// </summary>
    public sealed class MetinARPerfProbe : MonoBehaviour
    {
        public const float WindowSeconds = 5f;
        public const double FrameSpikeMs = 50.0;

        private static readonly object LatencyLock = new object();
        private static readonly Dictionary<string, FrameStatsAccumulator> Latencies = new Dictionary<string, FrameStatsAccumulator>();
        private static bool _active;

        private readonly FrameStatsAccumulator _frame = new FrameStatsAccumulator(FrameSpikeMs, 600);
        private readonly FrameStatsAccumulator _main = new FrameStatsAccumulator(FrameSpikeMs, 600);
        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _totalUsed;
        private ProfilerRecorder _gcReserved;
        private ProfilerRecorder _systemUsed;
        private float _windowStart;

        /// <summary>
        /// Report a latency sample in milliseconds (thread-safe). No-op unless the probe is active (development player).
        /// Metric names are sanitized to [A-Za-z0-9._-].
        /// </summary>
        public static void RecordLatency(string metric, double ms)
        {
            if (!_active)
            {
                return;
            }

            string key = "lat." + PerfLogLine.Sanitize(metric);
            lock (LatencyLock)
            {
                if (!Latencies.TryGetValue(key, out FrameStatsAccumulator acc))
                {
                    acc = new FrameStatsAccumulator(FrameSpikeMs, 128);
                    Latencies[key] = acc;
                }

                acc.Add(ms);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if (UNITY_ANDROID || UNITY_IOS) && DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (_active)
            {
                return;
            }

            var go = new GameObject("MetinARPerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<MetinARPerfProbe>();
#endif
        }

        private void OnEnable()
        {
            _active = true;
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _totalUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            _gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            _systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _windowStart = Time.realtimeSinceStartup;
            Debug.Log(PerfLogLine.Tag + " v=1 event=start mainThreadRecorder=" + (_mainThread.Valid ? 1 : 0)
                + " targetFps=" + Application.targetFrameRate + " device=" + PerfLogLine.Sanitize(SystemInfo.deviceModel)
                + " gpu=" + PerfLogLine.Sanitize(SystemInfo.graphicsDeviceName));
        }

        private void OnDisable()
        {
            _active = false;
            _mainThread.Dispose();
            _totalUsed.Dispose();
            _gcReserved.Dispose();
            _systemUsed.Dispose();
        }

        private void OnApplicationPause(bool paused)
        {
            // Do not count the background gap as a frame spike.
            Debug.Log(PerfLogLine.Tag + " v=1 event=" + (paused ? "pause" : "resume"));
            ResetWindow();
        }

        private void Update()
        {
            _frame.Add(Time.unscaledDeltaTime * 1000.0);
            if (_mainThread.Valid && _mainThread.Count > 0)
            {
                _main.Add(_mainThread.LastValue / 1e6); // ns -> ms
            }

            float now = Time.realtimeSinceStartup;
            if (now - _windowStart < WindowSeconds)
            {
                return;
            }

            var extra = new List<KeyValuePair<string, double>>(8);
            AddMemory(extra, "totalUsedMB", _totalUsed);
            AddMemory(extra, "gcReservedMB", _gcReserved);
            AddMemory(extra, "systemUsedMB", _systemUsed);
            lock (LatencyLock)
            {
                foreach (KeyValuePair<string, FrameStatsAccumulator> kv in Latencies)
                {
                    if (kv.Value.Count == 0)
                    {
                        continue;
                    }

                    FrameStatsSummary s = kv.Value.Summarize();
                    extra.Add(new KeyValuePair<string, double>(kv.Key + ".avgMs", s.AverageMs));
                    extra.Add(new KeyValuePair<string, double>(kv.Key + ".p95Ms", s.P95Ms));
                    extra.Add(new KeyValuePair<string, double>(kv.Key + ".n", s.Count));
                    kv.Value.Reset();
                }
            }

            Debug.Log(PerfLogLine.Format(SceneManager.GetActiveScene().name, now - _windowStart, _frame.Summarize(), _main.Summarize(), extra));
            ResetWindow();
        }

        private void ResetWindow()
        {
            _frame.Reset();
            _main.Reset();
            _windowStart = Time.realtimeSinceStartup;
        }

        private static void AddMemory(List<KeyValuePair<string, double>> extra, string key, ProfilerRecorder recorder)
        {
            if (recorder.Valid)
            {
                extra.Add(new KeyValuePair<string, double>(key, recorder.LastValue / (1024.0 * 1024.0)));
            }
        }
    }
}
