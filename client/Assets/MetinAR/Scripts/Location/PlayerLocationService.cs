using UnityEngine;

namespace MetinAR.Location
{
    /// <summary>
    /// Game-facing location façade. Owns a single <see cref="ILocationProvider"/> selected
    /// explicitly for the current platform (mock in Editor / non-mobile, Unity GPS on Android/iOS).
    /// Scene wiring via MonoBehaviour; not a global singleton with unrelated duties.
    /// </summary>
    public sealed class PlayerLocationService : MonoBehaviour
    {
        public enum ProviderKind
        {
            /// <summary>
            /// Editor → Mock; Android/iOS player → Unity; other platforms → Mock.
            /// </summary>
            Auto = 0,

            /// <summary>Force <see cref="MockLocationProvider"/> (Editor / tests).</summary>
            Mock = 1,

            /// <summary>Force <see cref="UnityLocationProvider"/> (device GPS / diagnostics).</summary>
            Unity = 2
        }

        [SerializeField]
        private ProviderKind _providerKind = ProviderKind.Auto;

        [SerializeField]
        private bool _startOnEnable = true;

        [SerializeField]
        private float _unityInitTimeoutSeconds = UnityLocationProvider.DefaultInitTimeoutSeconds;

        [SerializeField]
        private float _diagnosticLogIntervalSeconds = 2f;

        private ILocationProvider _provider;
        private UnityLocationProvider _unityProvider;
        private MockLocationProvider _mockProvider;
        private float _nextDiagnosticLogTime;

        /// <summary>
        /// Active provider state (never exposes Unity LocationServiceStatus).
        /// </summary>
        public LocationProviderState State =>
            _provider != null ? _provider.State : LocationProviderState.Stopped;

        /// <summary>
        /// The underlying provider instance after <see cref="Awake"/> / explicit selection.
        /// </summary>
        public ILocationProvider Provider => _provider;

        /// <summary>
        /// Mock provider when the mock path is selected; otherwise null.
        /// </summary>
        public MockLocationProvider MockProvider => _mockProvider;

        private void Awake()
        {
            SelectAndCreateProvider(_providerKind);
        }

        private void OnEnable()
        {
            if (_startOnEnable && _provider != null)
            {
                _provider.Start();
            }
        }

        private void OnDisable()
        {
            _provider?.Stop();
        }

        private void Update()
        {
            _unityProvider?.Tick();
            LogDiagnosticsOccasionally();
        }

        /// <summary>
        /// Explicitly select and recreate the provider. Stops the previous provider.
        /// </summary>
        public void SelectProvider(ProviderKind kind)
        {
            _provider?.Stop();
            _providerKind = kind;
            SelectAndCreateProvider(kind);
        }

        /// <summary>
        /// Starts the active provider.
        /// </summary>
        public void StartProvider()
        {
            EnsureProvider();
            _provider.Start();
        }

        /// <summary>
        /// Stops the active provider.
        /// </summary>
        public void StopProvider()
        {
            _provider?.Stop();
        }

        /// <summary>
        /// Returns the latest fix from the active provider when available.
        /// </summary>
        public bool TryGetLatestFix(out LocationFix fix)
        {
            if (_provider == null)
            {
                fix = default;
                return false;
            }

            return _provider.TryGetLatestFix(out fix);
        }

        private void EnsureProvider()
        {
            if (_provider == null)
            {
                SelectAndCreateProvider(_providerKind);
            }
        }

        private void SelectAndCreateProvider(ProviderKind kind)
        {
            ProviderKind resolved = ResolveKind(kind);
            _unityProvider = null;
            _mockProvider = null;

            if (resolved == ProviderKind.Mock)
            {
                _mockProvider = new MockLocationProvider();
                _provider = _mockProvider;
            }
            else
            {
                _unityProvider = new UnityLocationProvider(_unityInitTimeoutSeconds);
                _provider = _unityProvider;
            }
        }

        private static ProviderKind ResolveKind(ProviderKind kind)
        {
            if (kind == ProviderKind.Mock || kind == ProviderKind.Unity)
            {
                return kind;
            }

#if UNITY_EDITOR
            return ProviderKind.Mock;
#elif UNITY_ANDROID || UNITY_IOS
            return ProviderKind.Unity;
#else
            return ProviderKind.Mock;
#endif
        }

        private void LogDiagnosticsOccasionally()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_diagnosticLogIntervalSeconds <= 0f || _provider == null)
            {
                return;
            }

            if (Time.unscaledTime < _nextDiagnosticLogTime)
            {
                return;
            }

            _nextDiagnosticLogTime = Time.unscaledTime + _diagnosticLogIntervalSeconds;

            if (_provider.TryGetLatestFix(out LocationFix fix))
            {
                Debug.Log(
                    "[PlayerLocationService] state="
                    + _provider.State
                    + " lat="
                    + fix.Coordinate.Latitude.ToString("F6")
                    + " lon="
                    + fix.Coordinate.Longitude.ToString("F6")
                    + " acc_m="
                    + fix.HorizontalAccuracyMeters.ToString("F1"));
            }
            else
            {
                Debug.Log("[PlayerLocationService] state=" + _provider.State + " (no fix)");
            }
#endif
        }
    }
}
