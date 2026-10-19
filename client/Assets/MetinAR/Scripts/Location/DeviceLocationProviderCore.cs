using System;

namespace MetinAR.Location
{
    /// <summary>
    /// Foreground-only device location state machine behind <see cref="UnityLocationProvider"/>.
    /// Owns every permission and lifecycle decision; talks to the device only through
    /// <see cref="IDeviceLocationPlatform"/> so the real decision code is testable without Unity.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    /// <remarks>
    /// Permission flow (runtime-permission platforms, i.e. Android player):
    /// <list type="bullet">
    /// <item>Already granted (fine or coarse): no prompt; start the service if location is enabled.</item>
    /// <item>Otherwise ONE request for FINE + COARSE. The request is kept until its aggregate result is
    /// resolved. The first reported grant (fine or coarse, any order) starts acquisition once; a fine
    /// denial never cancels a coarse grant. Denial is final only when neither is granted (both denied,
    /// or dismissed with nothing granted).</item>
    /// <item>The init timeout is armed only when the platform service is actually started, never while
    /// the permission decision is pending.</item>
    /// <item><see cref="Stop"/> cancels the attempt: late results are recorded but never start
    /// acquisition. If the old dialog is still outstanding, a new <see cref="Start"/> waits for it
    /// instead of issuing a duplicate prompt.</item>
    /// <item>Results are evaluated on the caller's thread in <see cref="Tick"/>, <see cref="Resume"/> and
    /// <see cref="Start"/>; OS callbacks only record results.</item>
    /// </list>
    /// Lifecycle: <see cref="Suspend"/> stops the hardware and invalidates cached fixes; an in-flight
    /// permission request survives it (the Android dialog itself pauses the app). <see cref="Resume"/>
    /// restarts only an attempt that was requested and active before suspension and is still permitted;
    /// it never re-prompts.
    /// </remarks>
    public sealed class DeviceLocationProviderCore : ILocationProvider, ISuspendableLocationProvider
    {
        private readonly IDeviceLocationPlatform _platform;
        private readonly double _initTimeoutSeconds;
        private readonly float _desiredAccuracyMeters;
        private readonly float _updateDistanceMeters;

        private LocationProviderState _state = LocationProviderState.Stopped;
        private bool _acquisitionRequested;
        private bool _permissionCheckedThisAttempt;
        private bool _suspended;
        private bool _serviceStarted;
        private bool _initTimeoutArmed;
        private double _serviceStartRealtime;
        private LocationPermissionRequest _pendingRequest;
        private int _nextRequestId;
        private LocationFix _latestFix;
        private bool _hasFix;
        private LocationPermissionLevel _permissionLevel = LocationPermissionLevel.Unknown;

        public DeviceLocationProviderCore(
            IDeviceLocationPlatform platform,
            double initTimeoutSeconds,
            float desiredAccuracyMeters,
            float updateDistanceMeters)
        {
            if (platform == null)
            {
                throw new ArgumentNullException(nameof(platform));
            }

            if (double.IsNaN(initTimeoutSeconds) || double.IsInfinity(initTimeoutSeconds) || initTimeoutSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initTimeoutSeconds),
                    initTimeoutSeconds,
                    "Init timeout must be a finite value greater than 0 seconds.");
            }

            _platform = platform;
            _initTimeoutSeconds = initTimeoutSeconds;
            _desiredAccuracyMeters = desiredAccuracyMeters;
            _updateDistanceMeters = updateDistanceMeters;
        }

        /// <inheritdoc />
        public LocationProviderState State => _state;

        /// <inheritdoc />
        public bool IsSuspended => _suspended;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool AcquisitionRequested => _acquisitionRequested;

        /// <summary>True while an OS permission request has not reported its aggregate result.</summary>
        public bool IsPermissionRequestPending => _pendingRequest != null;

        /// <summary>True only after the platform service was started and until it stops/fails.</summary>
        public bool IsInitTimeoutArmed => _initTimeoutArmed;

        /// <summary>True while the platform location service has been started by this provider.</summary>
        public bool IsServiceStarted => _serviceStarted;

        /// <summary>
        /// Permission level actually granted (Android). <see cref="LocationPermissionLevel.Approximate"/>
        /// means the requested desired accuracy is NOT expected; read
        /// <see cref="LocationFix.HorizontalAccuracyMeters"/>. Unknown on platforms without runtime permissions.
        /// </summary>
        public LocationPermissionLevel PermissionLevel => _permissionLevel;

        /// <inheritdoc />
        public void Start()
        {
            if (_acquisitionRequested
                && (_state == LocationProviderState.Starting || _state == LocationProviderState.Running))
            {
                return;
            }

            _acquisitionRequested = true;
            _permissionCheckedThisAttempt = false;
            _hasFix = false;
            _initTimeoutArmed = false;
            _state = LocationProviderState.Starting;

            if (_suspended)
            {
                // Not in the foreground: Resume() begins the attempt (no prompt/service in background).
                return;
            }

            BeginAcquisition();
        }

        /// <inheritdoc />
        public void Stop()
        {
            _acquisitionRequested = false;
            _permissionCheckedThisAttempt = false;
            StopServiceIfNeeded();
            _state = LocationProviderState.Stopped;
            _hasFix = false;
            // An outstanding OS request (_pendingRequest) is intentionally kept: its late result is
            // ignored for this cancelled attempt, and a quick re-Start re-uses it instead of prompting twice.
        }

        /// <inheritdoc />
        public bool TryGetLatestFix(out LocationFix fix)
        {
            if (!_hasFix || _suspended || _state != LocationProviderState.Running)
            {
                fix = default;
                return false;
            }

            fix = _latestFix;
            return true;
        }

        /// <inheritdoc />
        public void Suspend()
        {
            if (_suspended)
            {
                return;
            }

            _suspended = true;
            bool wasActive = _acquisitionRequested
                             && (_state == LocationProviderState.Starting || _state == LocationProviderState.Running);
            StopServiceIfNeeded();
            _hasFix = false;
            if (wasActive)
            {
                // Still requested; acquisition resumes in the foreground.
                _state = LocationProviderState.Starting;
            }
        }

        /// <inheritdoc />
        public void Resume()
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            ProcessPendingPermissionResult();

            if (!_acquisitionRequested || _state != LocationProviderState.Starting || _serviceStarted)
            {
                // Stopped / denied / disabled / failed stay as they are; a started service needs nothing.
                return;
            }

            if (!_permissionCheckedThisAttempt)
            {
                // Start() arrived while suspended: this is the attempt's first foreground step.
                BeginAcquisition();
                return;
            }

            if (!_platform.RequiresRuntimePermission)
            {
                StartService();
                return;
            }

            if (HasAnyPermission())
            {
                StartServiceIfLocationEnabled();
                return;
            }

            if (_pendingRequest != null)
            {
                // The dialog's result is still outstanding: keep waiting, never prompt again.
                return;
            }

            // Permission no longer available for an already-checked attempt: no silent re-prompt.
            _permissionLevel = LocationPermissionLevel.None;
            _state = LocationProviderState.PermissionDenied;
        }

        /// <summary>
        /// Evaluate permission results and poll the platform service. Call once per frame.
        /// Does nothing while suspended. Never issues a permission request.
        /// </summary>
        public void Tick()
        {
            if (_suspended)
            {
                return;
            }

            ProcessPendingPermissionResult();

            if (_state == LocationProviderState.Starting)
            {
                if (!_serviceStarted)
                {
                    // Waiting for the permission decision: the init timeout stays disarmed.
                    return;
                }

                DeviceLocationServiceStatus status = _platform.ServiceStatus;
                if (status == DeviceLocationServiceStatus.Failed)
                {
                    FailService();
                    return;
                }

                if (status == DeviceLocationServiceStatus.Running)
                {
                    _state = LocationProviderState.Running;
                    CaptureLatestSample();
                    return;
                }

                if (_initTimeoutArmed && _platform.RealtimeSeconds - _serviceStartRealtime >= _initTimeoutSeconds)
                {
                    FailService();
                }

                return;
            }

            if (_state == LocationProviderState.Running)
            {
                if (_platform.ServiceStatus != DeviceLocationServiceStatus.Running)
                {
                    FailService();
                    return;
                }

                CaptureLatestSample();
            }
        }

        private void BeginAcquisition()
        {
            _permissionCheckedThisAttempt = true;

            if (!_platform.RequiresRuntimePermission)
            {
                // iOS / non-Android: the OS prompt is driven by the platform service start itself.
                _permissionLevel = LocationPermissionLevel.Unknown;
                StartService();
                return;
            }

            if (_pendingRequest != null && _pendingRequest.Snapshot().IsResolved)
            {
                // Result of a cancelled attempt: do not apply it to this new attempt.
                DropPendingRequest();
            }

            if (HasAnyPermission())
            {
                StartServiceIfLocationEnabled();
                return;
            }

            _state = LocationProviderState.Starting;
            _initTimeoutArmed = false;

            if (_pendingRequest != null)
            {
                // A dialog from a cancelled attempt is still open: wait for it, no duplicate prompt.
                return;
            }

            var request = new LocationPermissionRequest(++_nextRequestId);
            _pendingRequest = request;
            _platform.RequestLocationPermissions(request);
        }

        private void ProcessPendingPermissionResult()
        {
            LocationPermissionRequest request = _pendingRequest;
            if (request == null)
            {
                return;
            }

            LocationPermissionSnapshot result = request.Snapshot();
            if (result.IsResolved)
            {
                DropPendingRequest();
            }

            if (!_acquisitionRequested)
            {
                // Cancelled attempt (Stop): a late result never starts acquisition.
                return;
            }

            bool granted = result.AnyGranted || (result.IsResolved && HasAnyPermission());
            if (granted)
            {
                if (_serviceStarted)
                {
                    if (result.IsResolved)
                    {
                        UpdatePermissionLevelFromPlatform();
                    }

                    return;
                }

                if (_state == LocationProviderState.Starting && !_suspended)
                {
                    StartServiceIfLocationEnabled();
                }

                // While suspended the grant is remembered by the OS; Resume() starts in the foreground.
                return;
            }

            if (result.IsResolved && _state == LocationProviderState.Starting && !_serviceStarted)
            {
                _permissionLevel = LocationPermissionLevel.None;
                _state = LocationProviderState.PermissionDenied;
            }
        }

        private void DropPendingRequest()
        {
            if (_pendingRequest != null)
            {
                _pendingRequest.PlatformHandle = null;
                _pendingRequest = null;
            }
        }

        private bool HasAnyPermission()
        {
            return _platform.HasFineLocationPermission || _platform.HasCoarseLocationPermission;
        }

        private void UpdatePermissionLevelFromPlatform()
        {
            if (!_platform.RequiresRuntimePermission)
            {
                _permissionLevel = LocationPermissionLevel.Unknown;
            }
            else if (_platform.HasFineLocationPermission)
            {
                _permissionLevel = LocationPermissionLevel.Precise;
            }
            else if (_platform.HasCoarseLocationPermission)
            {
                _permissionLevel = LocationPermissionLevel.Approximate;
            }
            else
            {
                _permissionLevel = LocationPermissionLevel.None;
            }
        }

        private void StartServiceIfLocationEnabled()
        {
            UpdatePermissionLevelFromPlatform();
            if (!_platform.IsLocationEnabledByUser)
            {
                _state = LocationProviderState.LocationDisabled;
                return;
            }

            StartService();
        }

        private void StartService()
        {
            if (_serviceStarted)
            {
                return;
            }

            _platform.StartService(_desiredAccuracyMeters, _updateDistanceMeters);
            _serviceStarted = true;
            _state = LocationProviderState.Starting;
            _serviceStartRealtime = _platform.RealtimeSeconds;
            _initTimeoutArmed = true;
        }

        private void StopServiceIfNeeded()
        {
            if (_serviceStarted || _platform.ServiceStatus != DeviceLocationServiceStatus.Stopped)
            {
                _platform.StopService();
            }

            _serviceStarted = false;
            _initTimeoutArmed = false;
        }

        private void FailService()
        {
            StopServiceIfNeeded();
            _hasFix = false;
            _state = LocationProviderState.Failed;
        }

        private void CaptureLatestSample()
        {
            if (!_platform.TryGetLastSample(out DeviceLocationSample sample))
            {
                return;
            }

            double accuracy = sample.HorizontalAccuracyMeters;
            if (double.IsNaN(accuracy) || double.IsInfinity(accuracy) || accuracy < 0.0)
            {
                return;
            }

            double latitude = sample.Latitude;
            double longitude = sample.Longitude;
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
                _latestFix = new LocationFix(coordinate, accuracy, sample.TimestampSeconds);
                _hasFix = true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ignore transient invalid samples from the platform layer.
            }
        }
    }
}
