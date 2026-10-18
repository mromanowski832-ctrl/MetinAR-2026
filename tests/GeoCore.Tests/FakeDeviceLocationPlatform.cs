using System.Collections.Generic;
using MetinAR.Location;

namespace GeoCore.Tests
{
    /// <summary>
    /// Test double for the project's own <see cref="IDeviceLocationPlatform"/> seam (not UnityEngine).
    /// Records every platform call so tests can assert "exactly one request / one start".
    /// The decision code under test is the production <see cref="DeviceLocationProviderCore"/>.
    /// </summary>
    internal sealed class FakeDeviceLocationPlatform : IDeviceLocationPlatform
    {
        public bool RequiresRuntimePermission { get; set; } = true;

        public bool HasFineLocationPermission { get; set; }

        public bool HasCoarseLocationPermission { get; set; }

        public bool IsLocationEnabledByUser { get; set; } = true;

        public DeviceLocationServiceStatus ServiceStatus { get; set; } = DeviceLocationServiceStatus.Stopped;

        public double RealtimeSeconds { get; set; } = 100.0;

        public DeviceLocationSample Sample { get; set; } = new DeviceLocationSample(52.2297, 21.0122, 4.0, 1000.0);

        public List<LocationPermissionRequest> Requests { get; } = new List<LocationPermissionRequest>();

        public List<string[]> RequestedPermissionSets { get; } = new List<string[]>();

        public int StartServiceCount { get; private set; }

        public int StopServiceCount { get; private set; }

        public float LastDesiredAccuracy { get; private set; }

        public LocationPermissionRequest LastRequest => Requests.Count == 0 ? null : Requests[Requests.Count - 1];

        public void RequestLocationPermissions(LocationPermissionRequest request)
        {
            Requests.Add(request);
            RequestedPermissionSets.Add(request.Permissions);
            request.PlatformHandle = new object();
        }

        public void StartService(float desiredAccuracyMeters, float updateDistanceMeters)
        {
            StartServiceCount++;
            LastDesiredAccuracy = desiredAccuracyMeters;
            ServiceStatus = DeviceLocationServiceStatus.Initializing;
        }

        public void StopService()
        {
            StopServiceCount++;
            ServiceStatus = DeviceLocationServiceStatus.Stopped;
        }

        public bool TryGetLastSample(out DeviceLocationSample sample)
        {
            sample = Sample;
            return true;
        }

        /// <summary>The OS records the user's choice (as Android does before delivering callbacks).</summary>
        public void UserChooses(bool fine, bool coarse)
        {
            HasFineLocationPermission = fine;
            HasCoarseLocationPermission = coarse;
        }
    }
}
