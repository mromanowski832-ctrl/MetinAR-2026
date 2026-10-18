using System;

namespace MetinAR.Location
{
    /// <summary>Per-permission outcome reported by the OS for one request.</summary>
    public enum LocationPermissionResult
    {
        Pending = 0,
        Granted = 1,
        Denied = 2
    }

    /// <summary>
    /// Thread-safe snapshot of a <see cref="LocationPermissionRequest"/>.
    /// </summary>
    public readonly struct LocationPermissionSnapshot
    {
        public LocationPermissionSnapshot(
            LocationPermissionResult fine,
            LocationPermissionResult coarse,
            bool dismissed)
        {
            Fine = fine;
            Coarse = coarse;
            Dismissed = dismissed;
        }

        public LocationPermissionResult Fine { get; }

        public LocationPermissionResult Coarse { get; }

        public bool Dismissed { get; }

        /// <summary>True when either location permission was reported as granted.</summary>
        public bool AnyGranted =>
            Fine == LocationPermissionResult.Granted || Coarse == LocationPermissionResult.Granted;

        /// <summary>
        /// True when the whole operation is finished: both results reported, or the dialog was
        /// dismissed without an explicit choice.
        /// </summary>
        public bool IsResolved =>
            Dismissed
            || (Fine != LocationPermissionResult.Pending && Coarse != LocationPermissionResult.Pending);
    }

    /// <summary>
    /// One foreground runtime permission operation that asks for FINE and COARSE location together
    /// (Android guidance: request both in a single request when precise location is wanted, so the
    /// user can choose approximate-only access).
    /// The platform layer forwards the per-permission OS callbacks to
    /// <see cref="ReportGranted"/>, <see cref="ReportDenied"/> and <see cref="ReportDismissed"/>.
    /// Reports only record results (callbacks may arrive on a non-main thread); the owning
    /// <see cref="DeviceLocationProviderCore"/> evaluates the aggregate on its own thread.
    /// A fine denial never cancels a coarse grant; reports after resolution are ignored.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class LocationPermissionRequest
    {
        /// <summary>Android manifest/runtime name; equals UnityEngine.Android.Permission.FineLocation.</summary>
        public const string FineLocationPermission = "android.permission.ACCESS_FINE_LOCATION";

        /// <summary>Android manifest/runtime name; equals UnityEngine.Android.Permission.CoarseLocation.</summary>
        public const string CoarseLocationPermission = "android.permission.ACCESS_COARSE_LOCATION";

        private readonly object _gate = new object();
        private LocationPermissionResult _fine;
        private LocationPermissionResult _coarse;
        private bool _dismissed;

        public LocationPermissionRequest(int id)
        {
            Id = id;
        }

        /// <summary>Monotonic id assigned by the owning provider (diagnostics / tests).</summary>
        public int Id { get; }

        /// <summary>
        /// The permissions to pass to ONE platform request call, in order: FINE, COARSE.
        /// Returns a fresh array each time.
        /// </summary>
        public string[] Permissions => new[] { FineLocationPermission, CoarseLocationPermission };

        /// <summary>
        /// Platform object (e.g. Unity PermissionCallbacks) that must stay referenced until the
        /// aggregate result is resolved. Cleared by the owner when it drops the request.
        /// </summary>
        public object PlatformHandle { get; set; }

        /// <summary>Atomic copy of the results reported so far.</summary>
        public LocationPermissionSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new LocationPermissionSnapshot(_fine, _coarse, _dismissed);
            }
        }

        /// <summary>OS reported that <paramref name="permissionName"/> was granted.</summary>
        public void ReportGranted(string permissionName)
        {
            Report(permissionName, LocationPermissionResult.Granted);
        }

        /// <summary>OS reported that <paramref name="permissionName"/> was denied (incl. "don't ask again").</summary>
        public void ReportDenied(string permissionName)
        {
            Report(permissionName, LocationPermissionResult.Denied);
        }

        /// <summary>
        /// The dialog was dismissed without an explicit choice. Resolves the whole operation; results
        /// already reported (e.g. a coarse grant) are kept.
        /// </summary>
        public void ReportDismissed(string permissionName)
        {
            lock (_gate)
            {
                if (IsResolvedUnsafe())
                {
                    return;
                }

                _dismissed = true;
            }
        }

        private void Report(string permissionName, LocationPermissionResult result)
        {
            lock (_gate)
            {
                if (IsResolvedUnsafe())
                {
                    return;
                }

                if (string.Equals(permissionName, FineLocationPermission, StringComparison.Ordinal))
                {
                    if (_fine == LocationPermissionResult.Pending)
                    {
                        _fine = result;
                    }
                }
                else if (string.Equals(permissionName, CoarseLocationPermission, StringComparison.Ordinal))
                {
                    if (_coarse == LocationPermissionResult.Pending)
                    {
                        _coarse = result;
                    }
                }
            }
        }

        private bool IsResolvedUnsafe()
        {
            return new LocationPermissionSnapshot(_fine, _coarse, _dismissed).IsResolved;
        }
    }
}
