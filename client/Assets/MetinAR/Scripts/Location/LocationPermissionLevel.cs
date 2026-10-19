namespace MetinAR.Location
{
    /// <summary>
    /// Foreground location permission level actually granted by the OS.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public enum LocationPermissionLevel
    {
        /// <summary>
        /// Not known: no decision yet, or the platform does not expose it through Unity
        /// (iOS / Editor / mock). Never implies precise accuracy.
        /// </summary>
        Unknown = 0,

        /// <summary>Neither fine nor coarse location is granted.</summary>
        None = 1,

        /// <summary>
        /// Only approximate (coarse) location is granted. Fixes can be far less accurate than the
        /// requested desired accuracy; consumers must use <see cref="LocationFix.HorizontalAccuracyMeters"/>.
        /// </summary>
        Approximate = 2,

        /// <summary>Precise (fine) location is granted. Accuracy still depends on hardware/signal.</summary>
        Precise = 3
    }
}
