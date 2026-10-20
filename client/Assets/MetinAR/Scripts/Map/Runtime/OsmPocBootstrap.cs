using MetinAR.Location;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// Chooses the location source for the OSM POC scene before the first fix (the origin is captured once).
    /// Editor / batch: always the public demo landmark (no GPS). Device: the user picks
    /// "Use my GPS" (foreground-only Unity provider) or "Demo" (same public landmark, for repeatable
    /// performance runs that never touch the owner's location).
    /// The demo landmark is a public square (Rynek Główny, Kraków), not anybody's location.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class OsmPocBootstrap : MonoBehaviour
    {
        public enum Mode { Pending, Demo, Gps }

        /// <summary>West part of Rynek Główny, Kraków (public landmark, open pedestrian square).</summary>
        public const double DemoLatitude = 50.0614;
        public const double DemoLongitude = 19.9362;

        [SerializeField] private PlayerLocationService _location;
        [SerializeField] private GameObject _choicePanel;
        [SerializeField] private Button _gpsButton;
        [SerializeField] private Button _demoButton;
        [SerializeField] private bool _autoDemoInEditor = true;

        public Mode CurrentMode { get; private set; } = Mode.Pending;

        public static GeoCoordinate DemoCoordinate => new GeoCoordinate(DemoLatitude, DemoLongitude);

        public void Configure(PlayerLocationService location, GameObject choicePanel, Button gpsButton, Button demoButton)
        {
            _location = location;
            _choicePanel = choicePanel;
            _gpsButton = gpsButton;
            _demoButton = demoButton;
        }

        private void Start()
        {
            if (_gpsButton != null) _gpsButton.onClick.AddListener(StartGps);
            if (_demoButton != null) _demoButton.onClick.AddListener(StartDemo);

            if ((Application.isEditor && _autoDemoInEditor) || Application.isBatchMode || _choicePanel == null)
            {
                StartDemo();
            }
            else
            {
                _choicePanel.SetActive(true);
            }
        }

        public void StartDemo()
        {
            if (CurrentMode != Mode.Pending || _location == null)
            {
                return;
            }

            CurrentMode = Mode.Demo;
            HidePanel();
            _location.SelectProvider(PlayerLocationService.ProviderKind.Mock);
            _location.StartProvider();
            _location.MockProvider?.SetCoordinate(DemoCoordinate);
            Debug.Log("[OsmPoc] Location mode: demo landmark (no GPS).");
        }

        public void StartGps()
        {
            if (CurrentMode != Mode.Pending || _location == null)
            {
                return;
            }

            CurrentMode = Mode.Gps;
            HidePanel();
            _location.SelectProvider(PlayerLocationService.ProviderKind.Auto);
            _location.StartProvider();
            Debug.Log("[OsmPoc] Location mode: device GPS (foreground only).");
        }

        private void HidePanel()
        {
            if (_choicePanel != null)
            {
                _choicePanel.SetActive(false);
            }
        }
    }
}
