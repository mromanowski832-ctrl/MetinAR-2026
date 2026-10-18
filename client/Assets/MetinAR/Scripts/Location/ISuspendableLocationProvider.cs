namespace MetinAR.Location
{
    /// <summary>
    /// Provider that can be suspended while the application is paused/backgrounded and resumed
    /// in the foreground without losing an in-flight permission decision.
    /// </summary>
    public interface ISuspendableLocationProvider
    {
        /// <summary>True between <see cref="Suspend"/> and <see cref="Resume"/>.</summary>
        bool IsSuspended { get; }

        /// <summary>
        /// Stop hardware acquisition and invalidate cached fixes while the app is not in the foreground.
        /// An outstanding permission request is kept (its result is recorded but not acted on).
        /// </summary>
        void Suspend();

        /// <summary>
        /// Back in the foreground: restart acquisition only if it was requested and active before
        /// suspension and is still permitted. Never issues a repeated permission prompt.
        /// </summary>
        void Resume();
    }
}
