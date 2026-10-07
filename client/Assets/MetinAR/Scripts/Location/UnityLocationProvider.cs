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
        private bool _initTimeoutArmed;

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool _permissionRequestIssued;
        // Kept as a field so the callbacks object is not GC'd while the OS dialog is open.
        private PermissionCallbacks _pendingPermissionCallbacks;
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
            _initTimeoutArmed = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            _permissionRequestIssued = false;
            ClearPendingPermissionCallbacks();

            // Android: check runtime permission BEFORE isEnabledByUser so first-time users
            // get the system permission prompt instead of a premature LocationDisabled.
            if (HasAndroidLocationPermission())
            {
                if (!Input.location.isEnabledByUser)
                {
                    _state = LocationProviderState.LocationDisabled;
                    return;
                }

                BeginUnityLocationService();
                return;
            }

            RequestAndroidLocationPermissionOnce();
#else
            // iOS (and non-Android player): do NOT early-return on !isEnabledByUser before the
            // first Input.location.Start — on iOS that flag stays false until the OS prompt is
            // answered, so gating here would block the permission dialog forever.
            // Denied vs global location-off may not be separable without native APIs; we poll
            // LocationServiceStatus after Start and map Failed / timeout to Failed.
            BeginUnityLocationService();
#endif
        }

        /// <inheritdoc />
        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ClearPendingPermissionCallbacks();
            _permissionRequestIssued = false;
#endif
            _initTimeoutArmed = false;

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
        /// Poll LocationService status and refresh the latest fix.
        /// Intended to be called once per frame from a MonoBehaviour.
        /// Does not re-request Android permission (callbacks own that path).
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

            if (_state == LocationProviderState.Starting)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Still waiting for the permission dialog — do NOT arm / apply the 20s init timeout.
                if (!_initTimeoutArmed)
                {
                    return;
                }
#endif

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

                if (_initTimeoutArmed
                    && Time.realtimeSinceStartup - _startRealtime >= _initTimeoutSeconds)
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
        /// Issues at most one system permission request per <see cref="Start"/> attempt.
        /// </summary>
        private void RequestAndroidLocationPermissionOnce()
        {
            if (_permissionRequestIssued)
            {
                _state = LocationProviderState.PermissionDenied;
                return;
            }

            _permissionRequestIssued = true;
            _state = LocationProviderState.Starting;
            // Timeout stays disarmed until Input.location.Start is actually called.

            _pendingPermissionCallbacks = new PermissionCallbacks();
            _pendingPermissionCallbacks.PermissionGranted += OnAndroidPermissionGranted;
            _pendingPermissionCallbacks.PermissionDenied += OnAndroidPermissionDenied;
            _pendingPermissionCallbacks.PermissionDeniedAndDontAskAgain += OnAndroidPermissionDenied;
            _pendingPermissionCallbacks.PermissionRequestDismissed += OnAndroidPermissionDenied;

            Permission.RequestUserPermission(Permission.FineLocation, _pendingPermissionCallbacks);
        }

        private void OnAndroidPermissionGranted(string permissionName)
        {
            ClearPendingPermissionCallbacks();

            if (_state != LocationProviderState.Starting)
            {
                return;
            }

            if (!HasAndroidLocationPermission())
            {
                _state = LocationProviderState.PermissionDenied;
                return;
            }

            if (!Input.location.isEnabledByUser)
            {
                _state = LocationProviderState.LocationDisabled;
                return;
            }

            BeginUnityLocationService();
        }

        private void OnAndroidPermissionDenied(string permissionName)
        {
            ClearPendingPermissionCallbacks();

            if (_state == LocationProviderState.Starting)
            {
                _state = LocationProviderState.PermissionDenied;
            }
        }

        private void ClearPendingPermissionCallbacks()
        {
            if (_pendingPermissionCallbacks == null)
            {
                return;
            }

            _pendingPermissionCallbacks.PermissionGranted -= OnAndroidPermissionGranted;
            _pendingPermissionCallbacks.PermissionDenied -= OnAndroidPermissionDenied;
            _pendingPermissionCallbacks.PermissionDeniedAndDontAskAgain -= OnAndroidPermissionDenied;
            _pendingPermissionCallbacks.PermissionRequestDismissed -= OnAndroidPermissionDenied;
            _pendingPermissionCallbacks = null;
        }
#endif

        /// <summary>
        /// Starts <see cref="Input.location"/> and arms the init timeout.
        /// Caller must have already verified Android permission / availability where required.
        /// </summary>
        private void BeginUnityLocationService()
        {
            Input.location.Start(DesiredAccuracyMeters, UpdateDistanceMeters);
            _state = LocationProviderState.Starting;
            _startRealtime = Time.realtimeSinceStartup;
            _initTimeoutArmed = true;
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
