using MetinAR.Location;
using MetinAR.WorldMap;
using UnityEngine;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// METINAR-042 (#87) — NOT EDITOR-VERIFIED (written on the box without Unity; needs batch compile + Play Mode).
    /// Rotates the avatar visual to the walking direction from <see cref="AvatarHeadingEstimator"/>: GPS course
    /// while walking, compass when standing still (if enabled and available), otherwise the last heading.
    /// Foreground only: the compass needs no extra permission and is stopped in OnDisable.
    /// Logcat "[MapHeading]" prints only heading/source numbers, never coordinates.
    /// </summary>
    [DefaultExecutionOrder(95)]
    public sealed class MapAvatarHeadingView : MonoBehaviour
    {
        [SerializeField] private PlayerLocationService _location;
        [SerializeField] private WorldMapOrigin _origin;
        [SerializeField] private Transform _visual;
        [SerializeField] private bool _useCompassWhenStill = true;
        [SerializeField] private float _logIntervalSeconds = 10f;

        private readonly AvatarHeadingEstimator _estimator = new AvatarHeadingEstimator();
        private double _lastFixTimestamp = double.NaN;
        private float _logTimer;

        public AvatarHeadingEstimator Estimator => _estimator;

        public void Configure(PlayerLocationService location, WorldMapOrigin origin, Transform visual)
        {
            _location = location;
            _origin = origin;
            _visual = visual;
        }

        private void OnEnable()
        {
            if (_useCompassWhenStill)
            {
                Input.compass.enabled = true;
            }
        }

        private void OnDisable()
        {
            if (_useCompassWhenStill)
            {
                Input.compass.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_visual == null || _location == null || _origin == null || !_origin.HasOrigin)
            {
                return;
            }

            if (_location.TryGetLatestFix(out LocationFix fix) && fix.TimestampSeconds != _lastFixTimestamp)
            {
                _lastFixTimestamp = fix.TimestampSeconds;
                _origin.GetLocalMeters(fix.Coordinate, out double north, out double east);
                _estimator.OnFix(east, north, fix.HorizontalAccuracyMeters, Time.realtimeSinceStartupAsDouble);
            }

            double? compass = null;
            if (_useCompassWhenStill && Input.compass.enabled && Input.compass.timestamp > 0)
            {
                float h = Input.compass.trueHeading;
                compass = h > 0f ? h : Input.compass.magneticHeading;
            }

            _estimator.OnCompass(compass, Time.realtimeSinceStartupAsDouble);
            _estimator.Step(Time.unscaledDeltaTime);
            if (_estimator.HasHeading)
            {
                _visual.rotation = Quaternion.Euler(0f, (float)_estimator.VisualDegrees, 0f);
            }

            _logTimer += Time.unscaledDeltaTime;
            if (_logIntervalSeconds > 0f && _logTimer >= _logIntervalSeconds)
            {
                _logTimer = 0f;
                Debug.Log("[MapHeading] source=" + _estimator.Source + " target=" + _estimator.TargetDegrees.ToString("0")
                          + " visual=" + _estimator.VisualDegrees.ToString("0"));
            }
        }
    }
}
