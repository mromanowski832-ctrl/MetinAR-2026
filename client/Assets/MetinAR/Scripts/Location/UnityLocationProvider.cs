using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MetinAR.Location
{
    /// <summary>
    /// Foreground-only <see cref="ILocationProvider"/> wrapping <see cref="Input.location"/>.
    /// Requests a desired accuracy of ~5 m and a ~1 m distance filter, with an init timeout.
    /// The desired accuracy is a request, not a guarantee: with approximate (coarse-only) permission
    /// fixes are much less precise — see <see cref="PermissionLevel"/> and
    /// <see cref="LocationFix.HorizontalAccuracyMeters"/>.
    /// All permission/lifecycle decisions live in <see cref="DeviceLocationProviderCore"/>; this class only
    /// adapts Unity APIs (Android: one <c>Permission.RequestUserPermissions</c> call for FINE + COARSE).
    /// Call <see cref="Tick"/> from a MonoBehaviour Update; <see cref="Suspend"/>/<see cref="Resume"/> on
    /// application pause/resume (done by <see cref="PlayerLocationService"/>).
    /// Does not continuously log coordinates in release builds. No background location.
    /// </summary>
    public sealed class UnityLocationProvider : ILocationProvider, ISuspendableLocationProvider
    {
        /// <summary>Desired (requested) accuracy passed to Input.location.Start. Not guaranteed.</summary>
        public const float DesiredAccuracyMeters = 5f;
        public const float UpdateDistanceMeters = 1f;
        public const float DefaultInitTimeoutSeconds = 20f;

        private readonly DeviceLocationProviderCore _core;

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

            _core = new DeviceLocationProviderCore(
                new UnityDeviceLocationPlatform(),
                initTimeoutSeconds,
                DesiredAccuracyMeters,
                UpdateDistanceMeters);
        }

        /// <inheritdoc />
        public LocationProviderState State => _core.State;

        /// <inheritdoc />
        public bool IsSuspended => _core.IsSuspended;

        /// <summary>Granted permission level (Android); Unknown on iOS/Editor. Approximate ≠ 5 m accuracy.</summary>
        public LocationPermissionLevel PermissionLevel => _core.PermissionLevel;

        /// <summary>True while the Android permission dialog result is outstanding.</summary>
        public bool IsPermissionRequestPending => _core.IsPermissionRequestPending;

        /// <inheritdoc />
        public void Start()
        {
            _core.Start();
        }

        /// <inheritdoc />
        public void Stop()
        {
            _core.Stop();
        }

        /// <inheritdoc />
        public bool TryGetLatestFix(out LocationFix fix)
        {
            return _core.TryGetLatestFix(out fix);
        }

        /// <inheritdoc />
        public void Suspend()
        {
            _core.Suspend();
        }

        /// <inheritdoc />
        public void Resume()
        {
            _core.Resume();
        }

        /// <summary>
        /// Evaluate permission results, poll LocationService status and refresh the latest fix.
        /// Intended to be called once per frame from a MonoBehaviour. Never re-requests permission.
        /// </summary>
        public void Tick()
        {
            _core.Tick();
        }

        /// <summary>
        /// Unity API adapter. No decisions here — see <see cref="DeviceLocationProviderCore"/>.
        /// </summary>
        private sealed class UnityDeviceLocationPlatform : IDeviceLocationPlatform
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            public bool RequiresRuntimePermission => true;

            public bool HasFineLocationPermission => Permission.HasUserAuthorizedPermission(Permission.FineLocation);

            public bool HasCoarseLocationPermission => Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);

            public void RequestLocationPermissions(LocationPermissionRequest request)
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += request.ReportGranted;
                callbacks.PermissionDenied += request.ReportDenied;
                callbacks.PermissionDeniedAndDontAskAgain += request.ReportDenied;
                callbacks.PermissionRequestDismissed += request.ReportDismissed;

                // Retained (not GC'd) until the core resolves the aggregate result.
                request.PlatformHandle = callbacks;

                // ONE request for FINE + COARSE so the user can choose precise or approximate.
                Permission.RequestUserPermissions(request.Permissions, callbacks);
            }
#else
            // iOS / Editor / other: Unity's Input.location path drives any OS prompt itself.
            // Do NOT gate on isEnabledByUser before the first Input.location.Start (iOS keeps it false
            // until the prompt is answered). Denied vs location-off is not separable without native code.
            public bool RequiresRuntimePermission => false;

            public bool HasFineLocationPermission => false;

            public bool HasCoarseLocationPermission => false;

            public void RequestLocationPermissions(LocationPermissionRequest request)
            {
                throw new NotSupportedException("Runtime location permission requests are Android-only.");
            }
#endif

            public bool IsLocationEnabledByUser => Input.location.isEnabledByUser;

            public DeviceLocationServiceStatus ServiceStatus
            {
                get
                {
                    switch (Input.location.status)
                    {
                        case LocationServiceStatus.Initializing:
                            return DeviceLocationServiceStatus.Initializing;
                        case LocationServiceStatus.Running:
                            return DeviceLocationServiceStatus.Running;
                        case LocationServiceStatus.Failed:
                            return DeviceLocationServiceStatus.Failed;
                        default:
                            return DeviceLocationServiceStatus.Stopped;
                    }
                }
            }

            public void StartService(float desiredAccuracyMeters, float updateDistanceMeters)
            {
                Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);
            }

            public void StopService()
            {
                Input.location.Stop();
            }

            public bool TryGetLastSample(out DeviceLocationSample sample)
            {
                LocationInfo info = Input.location.lastData;
                sample = new DeviceLocationSample(info.latitude, info.longitude, info.horizontalAccuracy, info.timestamp);
                return true;
            }

            public double RealtimeSeconds => Time.realtimeSinceStartupAsDouble;
        }
    }
}
