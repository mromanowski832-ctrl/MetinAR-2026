using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace MetinAR.Location
{
    /// <summary>
    /// Foreground-only <see cref="ILocationProvider"/> wrapping <see cref="Input.location"/>.
    /// Desired accuracy ~5 m, distance filter ~1 m, with an init timeout.
    /// Call <see cref="Tick"/> from a MonoBehaviour Update while starting/running.
    /// Does not continuously log coordinates in release builds.
    /// </summary>
    public sealed class UnityLocationProvider : ILocationProvider
    {
        public const float DesiredAccuracyMeters = 5f;
        public const float UpdateDistanceMeters = 1f;
        public const float DefaultInitTimeoutSeconds = 20f;

        private readonly float _initTimeoutSeconds;
        private LocationProviderState _state = LocationProviderState.Stopped;
        private LocationFix _latestFix;
        private bool _hasFix;
        private float _startRealtime;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool _permissionRequestIssued;
#endif

        public UnityLocationProvider()
            : this(DefaultInitTimeoutSeconds)
        {
        }

        public UnityLocationProvider(float initTimeoutSeconds)
        {
            if (float.IsNaN(initTimeoutSeconds)
                || float.IsInfinity(initTimeoutSeconds)
                || initTimeoutSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initTimeoutSeconds),
                    initTimeoutSeconds,
                    "Init timeout must be a finite value greater than 0 seconds.");
            }

            _initTimeoutSeconds = initTimeoutSeconds;
        }

        /// <inheritdoc />
        public LocationProviderState State => _state;

        /// <inheritdoc />
        public void Start()
        {
            if (_state == LocationProviderState.Starting || _state == LocationProviderState.Running)
            {
                return;
            }

            _hasFix = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            _permissionRequestIssued = false;
#endif

            if (!Input.location.isEnabledByUser)
            {
                _state = LocationProviderState.LocationDisabled;
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EnsureAndroidLocationPermission())
            {
                return;
            }
#endif

            BeginUnityLocationService();
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (Input.location.status != LocationServiceStatus.Stopped)
            {
                Input.location.Stop();
            }

            _state = LocationProviderState.Stopped;
            _hasFix = false;
        }

        /// <inheritdoc />
        public bool TryGetLatestFix(out LocationFix fix)
        {
            if (!_hasFix || _state != LocationProviderState.Running)
            {
                fix = default;
                return false;
            }

            fix = _latestFix;
            return true;
        }

        /// <summary>
        /// Poll permission / LocationService status and refresh the latest fix.
        /// Intended to be called once per frame from a MonoBehaviour.
        /// </summary>
        public void Tick()
        {
            if (_state == LocationProviderState.Stopped
                || _state == LocationProviderState.Failed
                || _state == LocationProviderState.PermissionDenied
                || _state == LocationProviderState.LocationDisabled)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (_state == LocationProviderState.Starting)
            {
                if (!Input.location.isEnabledByUser)
                {
                    _state = LocationProviderState.LocationDisabled;
                    return;
                }

                if (Input.location.status == LocationServiceStatus.Stopped)
                {
                    if (!HasAndroidLocationPermission())
                    {
                        if (_permissionRequestIssued)
                        {
                            if (Time.realtimeSinceStartup - _startRealtime >= _initTimeoutSeconds)
                            {
                                _state = LocationProviderState.PermissionDenied;
                            }
                        }
                        else if (!EnsureAndroidLocationPermission())
                        {
                            return;
                        }
                        else
                        {
                            BeginUnityLocationService();
                        }

                        return;
                    }

                    BeginUnityLocationService();
                    return;
                }
            }
#endif

            if (_state == LocationProviderState.Starting)
            {
                LocationServiceStatus status = Input.location.status;

                if (status == LocationServiceStatus.Failed)
                {
                    _state = LocationProviderState.Failed;
                    return;
                }

                if (status == LocationServiceStatus.Running)
                {
                    _state = LocationProviderState.Running;
                    CaptureLastData();
                    return;
                }

                if (Time.realtimeSinceStartup - _startRealtime >= _initTimeoutSeconds)
                {
                    Input.location.Stop();
                    _state = LocationProviderState.Failed;
                    return;
                }

                return;
            }

            if (_state == LocationProviderState.Running)
            {
                if (Input.location.status != LocationServiceStatus.Running)
                {
                    _state = LocationProviderState.Failed;
                    return;
                }

                CaptureLastData();
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool HasAndroidLocationPermission()
        {
            return Permission.HasUserAuthorizedPermission(Permission.FineLocation)
                   || Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
        }

        /// <summary>
        /// Ensures Android fine location permission without spamming requests.
        /// Requests at most once per <see cref="Start"/> attempt.
        /// </summary>
        /// <returns>True when permission is already granted.</returns>
        private bool EnsureAndroidLocationPermission()
        {
            if (HasAndroidLocationPermission())
            {
                return true;
            }

            if (!_permissionRequestIssued)
            {
                Permission.RequestUserPermission(Permission.FineLocation);
                _permissionRequestIssued = true;
                _state = LocationProviderState.Starting;
                _startRealtime = Time.realtimeSinceStartup;
            }
            else
            {
                _state = LocationProviderState.PermissionDenied;
            }

            return false;
        }
#endif

        private void BeginUnityLocationService()
        {
            if (!Input.location.isEnabledByUser)
            {
                _state = LocationProviderState.LocationDisabled;
                return;
            }

            Input.location.Start(DesiredAccuracyMeters, UpdateDistanceMeters);
            _state = LocationProviderState.Starting;
            _startRealtime = Time.realtimeSinceStartup;
        }

        private void CaptureLastData()
        {
            LocationInfo info = Input.location.lastData;

            double accuracy = info.horizontalAccuracy;
            if (double.IsNaN(accuracy) || double.IsInfinity(accuracy) || accuracy < 0.0)
            {
                return;
            }

            double latitude = info.latitude;
            double longitude = info.longitude;
            if (double.IsNaN(latitude) || double.IsInfinity(latitude)
                || double.IsNaN(longitude) || double.IsInfinity(longitude)
                || latitude < -90.0 || latitude > 90.0
                || longitude < -180.0 || longitude > 180.0)
            {
                return;
            }

            try
            {
                var coordinate = new GeoCoordinate(latitude, longitude);
                _latestFix = new LocationFix(coordinate, accuracy, info.timestamp);
                _hasFix = true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ignore transient invalid samples from the platform layer.
            }
        }
    }
}
