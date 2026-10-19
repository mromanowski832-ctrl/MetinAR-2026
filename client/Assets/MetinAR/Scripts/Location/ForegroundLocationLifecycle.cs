namespace MetinAR.Location
{
    /// <summary>
    /// Foreground-only acquisition policy used by <see cref="PlayerLocationService"/>.
    /// Tracks the caller's intent (start requested / explicitly stopped), component enablement and
    /// application pause, and drives the provider accordingly:
    /// <list type="bullet">
    /// <item>Pause: suspend (<see cref="ISuspendableLocationProvider"/>) or stop the provider, so no
    /// acquisition runs and no cached fix is usable while the app is not in the foreground.</item>
    /// <item>Resume: restart only if acquisition was requested before the pause, the component is
    /// still enabled, and (for device providers) permission still allows it. Explicit stop, a disabled
    /// component or a denied permission never restart silently.</item>
    /// <item>A start requested while paused is deferred to the next resume.</item>
    /// </list>
    /// The world origin is not owned here and is never reset by pause/resume.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class ForegroundLocationLifecycle
    {
        private ILocationProvider _provider;
        private bool _acquisitionRequested;
        private bool _componentEnabled;
        private bool _applicationPaused;
        private bool _restartAfterPause;
        private bool _startDeferredUntilResume;

        /// <summary>Provider currently driven by this policy.</summary>
        public ILocationProvider Provider => _provider;

        /// <summary>True after <see cref="RequestStart"/> until <see cref="RequestStop"/> / disable.</summary>
        public bool AcquisitionRequested => _acquisitionRequested;

        public bool ComponentEnabled => _componentEnabled;

        public bool ApplicationPaused => _applicationPaused;

        /// <summary>
        /// Attach a (new) provider. Callers stop the previous provider first via <see cref="RequestStop"/>.
        /// A provider attached while paused is suspended immediately.
        /// </summary>
        public void AttachProvider(ILocationProvider provider)
        {
            _provider = provider;
            _restartAfterPause = false;
            _startDeferredUntilResume = false;
            if (_applicationPaused && provider is ISuspendableLocationProvider suspendable)
            {
                suspendable.Suspend();
            }
        }

        /// <summary>Component enabled/disabled. Disabling is an implicit stop (intent cleared).</summary>
        public void SetComponentEnabled(bool enabled)
        {
            _componentEnabled = enabled;
            if (!enabled)
            {
                RequestStop();
            }
        }

        /// <summary>Request acquisition. Deferred until resume while the application is paused.</summary>
        public void RequestStart()
        {
            if (_provider == null)
            {
                return;
            }

            _acquisitionRequested = true;
            if (_applicationPaused)
            {
                _startDeferredUntilResume = true;
                return;
            }

            _provider.Start();
        }

        /// <summary>Explicit stop: acquisition stays off until the next <see cref="RequestStart"/>.</summary>
        public void RequestStop()
        {
            _acquisitionRequested = false;
            _restartAfterPause = false;
            _startDeferredUntilResume = false;
            _provider?.Stop();
        }

        /// <summary>Application pause (true) / resume (false). Repeated identical calls are ignored.</summary>
        public void SetApplicationPaused(bool paused)
        {
            if (paused == _applicationPaused)
            {
                return;
            }

            _applicationPaused = paused;
            if (_provider == null)
            {
                return;
            }

            if (paused)
            {
                if (_provider is ISuspendableLocationProvider suspendable)
                {
                    suspendable.Suspend();
                }
                else
                {
                    LocationProviderState state = _provider.State;
                    _restartAfterPause = _acquisitionRequested
                                         && (state == LocationProviderState.Starting || state == LocationProviderState.Running);
                    if (state != LocationProviderState.Stopped)
                    {
                        _provider.Stop();
                    }
                }

                return;
            }

            bool mayResume = _acquisitionRequested && _componentEnabled;
            if (_provider is ISuspendableLocationProvider resumable)
            {
                if (!mayResume && _provider.State != LocationProviderState.Stopped)
                {
                    _provider.Stop();
                }

                resumable.Resume();
            }
            else if (_restartAfterPause && mayResume)
            {
                _provider.Start();
            }

            _restartAfterPause = false;
            if (_startDeferredUntilResume)
            {
                _startDeferredUntilResume = false;
                if (mayResume)
                {
                    _provider.Start();
                }
            }
        }
    }
}
