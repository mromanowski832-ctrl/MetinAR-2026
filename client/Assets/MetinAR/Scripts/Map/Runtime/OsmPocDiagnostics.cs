using System;
using System.Text;
using MetinAR.WorldMap;
using TMPro;
using Unity.Profiling;
using UnityEngine;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// P8 evidence overlay + logcat line for the OSM POC. Shows FPS / frame time, draw calls, memory,
    /// map pipeline stats and the spawn-safety verdict for the mob and NPC positions.
    /// Never prints coordinates or tile ids. Logcat filter: "[OsmPocPerf]".
    /// </summary>
    public sealed class OsmPocDiagnostics : MonoBehaviour
    {
        private const int Window = 240;

        [SerializeField] private OsmMapView _map;
        [SerializeField] private OsmPocBootstrap _bootstrap;
        [SerializeField] private WorldMapEntity _mob;
        [SerializeField] private WorldMapEntity _npc;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private float _logIntervalSeconds = 5f;
        [SerializeField] private float _safetyIntervalSeconds = 0.5f;

        private readonly float[] _frames = new float[Window];
        private readonly float[] _sorted = new float[Window];
        private int _frameCount;
        private int _frameIndex;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _usedMemory;
        private ProfilerRecorder _gcMemory;
        private float _nextLog;
        private float _nextSafety;
        private float _nextLabel;
        private readonly StringBuilder _sb = new StringBuilder(512);

        public SafetyResult MobSafety { get; private set; } = new SafetyResult(SafetyVerdict.Unknown, SafetyReason.NoData, "not evaluated");
        public SafetyResult NpcSafety { get; private set; } = new SafetyResult(SafetyVerdict.Unknown, SafetyReason.NoData, "not evaluated");
        public float AverageFrameMs { get; private set; }
        public float P95FrameMs { get; private set; }
        public float MaxFrameMs { get; private set; }
        public long DrawCalls { get; private set; } = -1;
        public long SetPassCalls { get; private set; } = -1;
        public long UsedMemoryBytes { get; private set; } = -1;
        public string LastLine { get; private set; } = string.Empty;

        public void Configure(OsmMapView map, OsmPocBootstrap bootstrap, WorldMapEntity mob, WorldMapEntity npc, TextMeshProUGUI label)
        {
            _map = map;
            _bootstrap = bootstrap;
            _mob = mob;
            _npc = npc;
            _label = label;
        }

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            _gcMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _setPass.Dispose();
            _usedMemory.Dispose();
            _gcMemory.Dispose();
        }

        private void Update()
        {
            _frames[_frameIndex] = Time.unscaledDeltaTime * 1000f;
            _frameIndex = (_frameIndex + 1) % Window;
            _frameCount = Math.Min(_frameCount + 1, Window);

            float now = Time.unscaledTime;
            if (now >= _nextSafety)
            {
                _nextSafety = now + _safetyIntervalSeconds;
                EvaluateSafety();
            }

            if (now >= _nextLabel)
            {
                _nextLabel = now + 0.5f;
                ComputeFrameStats();
                SampleRecorders();
                LastLine = BuildLine();
                if (_label != null)
                {
                    _label.text = LastLine.Replace(" | ", "\n");
                }
            }

            if (now >= _nextLog)
            {
                _nextLog = now + _logIntervalSeconds;
                Debug.Log("[OsmPocPerf] " + LastLine);
            }
        }

        private void EvaluateSafety()
        {
            if (_map == null)
            {
                return;
            }

            if (_mob != null && _mob.IsPlaced)
            {
                MobSafety = _map.Safety.Evaluate(_mob.Coordinate, 0);
            }

            if (_npc != null && _npc.IsPlaced)
            {
                NpcSafety = _map.Safety.Evaluate(_npc.Coordinate, 0);
            }
        }

        private void ComputeFrameStats()
        {
            if (_frameCount == 0)
            {
                return;
            }

            float sum = 0f, max = 0f;
            for (int i = 0; i < _frameCount; i++)
            {
                _sorted[i] = _frames[i];
                sum += _frames[i];
                max = Mathf.Max(max, _frames[i]);
            }

            Array.Sort(_sorted, 0, _frameCount);
            AverageFrameMs = sum / _frameCount;
            P95FrameMs = _sorted[Mathf.Clamp((int)(_frameCount * 0.95f), 0, _frameCount - 1)];
            MaxFrameMs = max;
        }

        private void SampleRecorders()
        {
            DrawCalls = _drawCalls.Valid ? _drawCalls.LastValue : -1;
            SetPassCalls = _setPass.Valid ? _setPass.LastValue : -1;
            UsedMemoryBytes = _usedMemory.Valid ? _usedMemory.LastValue : -1;
        }

        private string BuildLine()
        {
            _sb.Clear();
            float fps = AverageFrameMs > 0f ? 1000f / AverageFrameMs : 0f;
            _sb.Append("fps=").Append(fps.ToString("F1"))
                .Append(" frameMs avg=").Append(AverageFrameMs.ToString("F1"))
                .Append(" p95=").Append(P95FrameMs.ToString("F1"))
                .Append(" max=").Append(MaxFrameMs.ToString("F1"));
            _sb.Append(" | drawCalls=").Append(DrawCalls).Append(" setPass=").Append(SetPassCalls)
                .Append(" usedMemMB=").Append(UsedMemoryBytes < 0 ? -1 : UsedMemoryBytes / (1024 * 1024))
                .Append(" gcMB=").Append(_gcMemory.Valid ? _gcMemory.LastValue / (1024 * 1024) : -1);
            if (_map != null)
            {
                MapViewStats s = _map.Stats;
                _sb.Append(" | map ").Append(s.Summary());
                if (_map.IsSourceFailed)
                {
                    _sb.Append(" | map source unavailable (offline?)");
                }
            }

            _sb.Append(" | spawn mob=").Append(MobSafety.ToString()).Append(" npc=").Append(NpcSafety.ToString());
            if (_bootstrap != null)
            {
                _sb.Append(" | mode=").Append(_bootstrap.CurrentMode);
            }

            return _sb.ToString();
        }
    }
}
