namespace MetinAR.Location
{
    /// <summary>
    /// Abstraction over device GPS / mock location sources.
    /// </summary>
    public interface ILocationProvider
    {
        /// <summary>
        /// Current provider lifecycle / error state.
        /// </summary>
        LocationProviderState State { get; }

        /// <summary>
        /// Returns the latest valid fix when available.
        /// </summary>
        /// <param name="fix">Receives the latest fix when the method returns true.</param>
        /// <returns>True when a fix is available; otherwise false.</returns>
        bool TryGetLatestFix(out LocationFix fix);

        /// <summary>
        /// Begins location acquisition (permissions / hardware start as needed).
        /// </summary>
        void Start();

        /// <summary>
        /// Stops location acquisition and returns to <see cref="LocationProviderState.Stopped"/>.
        /// </summary>
        void Stop();
    }
}
