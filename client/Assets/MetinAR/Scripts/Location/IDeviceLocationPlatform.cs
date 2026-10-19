namespace MetinAR.Location
{
    /// <summary>
    /// Game-facing mirror of the platform location service status (keeps UnityEngine types out of
    /// <see cref="DeviceLocationProviderCore"/>).
    /// </summary>
    public enum DeviceLocationServiceStatus
    {
        Stopped = 0,
        Initializing = 1,
        Running = 2,
        Failed = 3
    }

    /// <summary>Raw platform sample; validated by <see cref="DeviceLocationProviderCore"/>.</summary>
    public readonly struct DeviceLocationSample
    {
        public DeviceLocationSample(
            double latitude,
            double longitude,
            double horizontalAccuracyMeters,
            double timestampSeconds)
        {
            Latitude = latitude;
            Longitude = longitude;
            HorizontalAccuracyMeters = horizontalAccuracyMeters;
            TimestampSeconds = timestampSeconds;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public double HorizontalAccuracyMeters { get; }

        public double TimestampSeconds { get; }
    }

    /// <summary>
    /// Thin seam over the device location APIs (Unity <c>Input.location</c> and
    /// <c>UnityEngine.Android.Permission</c> in production). It performs no decisions; all
    /// permission/lifecycle decisions live in <see cref="DeviceLocationProviderCore"/>.
    /// Pure C# interface — no UnityEngine dependency.
    /// </summary>
    public interface IDeviceLocationPlatform
    {
        /// <summary>True when a runtime permission request is needed before starting (Android player).</summary>
        bool RequiresRuntimePermission { get; }

        bool HasFineLocationPermission { get; }

        bool HasCoarseLocationPermission { get; }

        /// <summary>
        /// Issues ONE OS request for <see cref="LocationPermissionRequest.Permissions"/> (FINE + COARSE)
        /// and forwards every per-permission callback to <paramref name="request"/>. Any callback object
        /// must be stored in <see cref="LocationPermissionRequest.PlatformHandle"/>.
        /// </summary>
        void RequestLocationPermissions(LocationPermissionRequest request);

        /// <summary>Device location services enabled by the user/system.</summary>
        bool IsLocationEnabledByUser { get; }

        DeviceLocationServiceStatus ServiceStatus { get; }

        void StartService(float desiredAccuracyMeters, float updateDistanceMeters);

        void StopService();

        bool TryGetLastSample(out DeviceLocationSample sample);

        /// <summary>Monotonic real time in seconds (unscaled).</summary>
        double RealtimeSeconds { get; }
    }
}
