using System.Text;
using MetinAR.Location;
using MetinAR.WorldMap;
using UnityEngine;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// METINAR-008B POC only: smooths the *visual* player marker on the OSM map. The raw GPS position stays on
    /// the Player transform (moved by <see cref="PlayerMapAvatar"/>) and is what gameplay (spawn distances, map
    /// streaming) uses; this component only moves a child "Visual" anchor that carries the model and the camera
    /// target. Runs after PlayerMapAvatar.Update and before <see cref="MapCameraRig"/> (order 100).
    /// Logcat filter "[OsmPocAvatar]": per-fix deltas, accuracy and timings only, never coordinates.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class OsmPocAvatarSmoothing : MonoBehaviour
    {
        [SerializeField] private PlayerLocationService _location;
        [SerializeField] private WorldMapOrigin _origin;
        [SerializeField] private Transform _rawPlayer;
        [SerializeField] private Transform _visual;
        [SerializeField] private bool _smoothingEnabled = true;
        [SerializeField] private bool _logEachFix = true;
        [SerializeField] private float _summaryIntervalSeconds = 10f;

        private readonly AvatarPositionSmoother _smoother = new AvatarPositionSmoother();
        private readonly int[] _counts = new int[8];
        private bool _hasPrevRaw;
        private double _prevRawE, _prevRawN, _prevRawT;
        private float _summaryTimer;
        private double _maxLag, _maxVisualStep, _maxRawStep;
        private int _prevTimeouts;

        public AvatarPositionSmoother Smoother => _smoother;
        public Transform Visual => _visual;
        public bool SmoothingEnabled { get => _smoothingEnabled; set => _smoothingEnabled = value; }
        public AvatarFixResult LastResult { get; private set; }
        public int FixCount { get; private set; }

        public void Configure(PlayerLocationService location, WorldMapOrigin origin, Transform rawPlayer, Transform visual)
        {
            _location = location;
            _origin = origin;
            _rawPlayer = rawPlayer;
            _visual = visual;
        }

        private void LateUpdate()
        {
            if (_visual == null || _rawPlayer == null)
            {
                return;
            }

            float y = _rawPlayer.position.y;
            if (!_smoothingEnabled || _location == null || _origin == null || !_origin.HasOrigin)
            {
                _visual.localPosition = Vector3.zero;
                return;
            }

            if (_location.TryGetLatestFix(out LocationFix fix))
            {
                Vector3 local = _origin.WorldToLocal(fix.Coordinate, y);
                AvatarFixResult r = _smoother.OnFix(local.x, local.z, fix.HorizontalAccuracyMeters, fix.TimestampSeconds);
                if (r != AvatarFixResult.Duplicate)
                {
                    OnNewSample(r, local.x, local.z, fix.HorizontalAccuracyMeters, fix.TimestampSeconds);
                }
            }

            if (!_smoother.HasPosition)
            {
                _visual.localPosition = Vector3.zero;
                return;
            }

            double step = _smoother.Step(Time.unscaledDeltaTime);
            if (_smoother.PendingTimeouts != _prevTimeouts)
            {
                _prevTimeouts = _smoother.PendingTimeouts;
                Log("pending_timeout accepted=1 lagM=" + F(_smoother.LagMeters));
            }

            if (step > _maxVisualStep) _maxVisualStep = step;
            if (_smoother.LagMeters > _maxLag) _maxLag = _smoother.LagMeters;
            _visual.position = new Vector3((float)_smoother.VisualEast, y, (float)_smoother.VisualNorth);

            _summaryTimer += Time.unscaledDeltaTime;
            if (_summaryIntervalSeconds > 0f && _summaryTimer >= _summaryIntervalSeconds)
            {
                _summaryTimer = 0f;
                LogSummary();
            }
        }

        private void OnNewSample(AvatarFixResult r, double e, double n, double acc, double t)
        {
            LastResult = r;
            FixCount++;
            _counts[(int)r]++;
            double dt = 0, stepM = 0, speed = 0;
            if (_hasPrevRaw)
            {
                dt = t - _prevRawT;
                double dx = e - _prevRawE, dz = n - _prevRawN;
                stepM = System.Math.Sqrt(dx * dx + dz * dz);
                speed = dt > 0 ? stepM / dt : 0;
                if (stepM > _maxRawStep) _maxRawStep = stepM;
            }

            _hasPrevRaw = true;
            _prevRawE = e;
            _prevRawN = n;
            _prevRawT = t;
            if (_logEachFix)
            {
                Log("fix result=" + r + " dtFix=" + F(dt) + " stepM=" + F(stepM) + " rawSpeed=" + F(speed) +
                    " acc_m=" + F(acc) + " jitterThrM=" + F(_smoother.JitterThreshold(acc)) + " lagM=" + F(_smoother.LagMeters));
            }
        }

        private void LogSummary()
        {
            var sb = new StringBuilder("summary fixes=").Append(FixCount);
            for (int i = 0; i < _counts.Length; i++)
            {
                if (_counts[i] > 0)
                {
                    sb.Append(' ').Append((AvatarFixResult)i).Append('=').Append(_counts[i]);
                }
            }

            sb.Append(" maxRawStepM=").Append(F(_maxRawStep))
              .Append(" maxVisualFrameStepM=").Append(F(_maxVisualStep))
              .Append(" maxLagM=").Append(F(_maxLag))
              .Append(" pendingTimeouts=").Append(_smoother.PendingTimeouts);
            Log(sb.ToString());
            _maxLag = _maxVisualStep = _maxRawStep = 0;
        }

        private static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        private static void Log(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[OsmPocAvatar] " + message);
#endif
        }
    }
}
