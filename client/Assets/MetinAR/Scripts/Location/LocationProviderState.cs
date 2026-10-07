namespace MetinAR.Location
{
    /// <summary>
    /// Game-facing location provider lifecycle / error state.
    /// Intentionally does not expose UnityEngine.LocationServiceStatus.
    /// </summary>
    public enum LocationProviderState
    {
        /// <summary>Provider is idle; location service is not active.</summary>
        Stopped = 0,

        /// <summary>Start requested; waiting for permission, hardware init, or first fix.</summary>
        Starting = 1,

        /// <summary>Provider is active and can supply fixes.</summary>
        Running = 2,

        /// <summary>OS denied location permission for the app.</summary>
        PermissionDenied = 3,

        /// <summary>Device location (GPS/network) is disabled by the user/system.</summary>
        LocationDisabled = 4,

        /// <summary>Start timed out or the underlying service failed.</summary>
        Failed = 5
    }
}
