using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// Always-visible data attribution required by ODbL / OpenMapTiles / OpenFreeMap, plus an (i) panel with
    /// the full credits and a link to openstreetmap.org/copyright. Text comes from the active tile source
    /// (TileJSON attribution), falling back to the static default.
    /// </summary>
    public sealed class MapAttributionView : MonoBehaviour
    {
        [SerializeField] private OsmMapView _map;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private Button _infoButton;
        [SerializeField] private GameObject _panel;
        [SerializeField] private TextMeshProUGUI _panelText;
        [SerializeField] private Button _openCopyrightButton;
        [SerializeField] private Button _closeButton;

        private IReadOnlyList<string> _shown;

        public string LabelText => _label != null ? _label.text : string.Empty;
        public GameObject Panel => _panel;
        public Button InfoButton => _infoButton;
        public Button CloseButton => _closeButton;

        public void Configure(OsmMapView map, TextMeshProUGUI label, Button infoButton, GameObject panel,
            TextMeshProUGUI panelText, Button openCopyrightButton, Button closeButton)
        {
            _map = map;
            _label = label;
            _infoButton = infoButton;
            _panel = panel;
            _panelText = panelText;
            _openCopyrightButton = openCopyrightButton;
            _closeButton = closeButton;
        }

        private void Awake()
        {
            if (_infoButton != null) _infoButton.onClick.AddListener(() => SetPanel(true));
            if (_closeButton != null) _closeButton.onClick.AddListener(() => SetPanel(false));
            if (_openCopyrightButton != null)
            {
                _openCopyrightButton.onClick.AddListener(() =>
                    Application.OpenURL(_map != null ? _map.AttributionUrl : Osm.OpenFreeMapTileSource.OsmCopyrightUrl));
            }

            SetPanel(false);
            Refresh(true);
        }

        private void Update()
        {
            Refresh(false);
        }

        public void SetPanel(bool open)
        {
            if (_panel != null)
            {
                _panel.SetActive(open);
            }
        }

        private void Refresh(bool force)
        {
            IReadOnlyList<string> lines = _map != null ? _map.Attribution : Osm.OpenFreeMapTileSource.DefaultAttribution;
            if (!force && ReferenceEquals(lines, _shown))
            {
                return;
            }

            _shown = lines;
            List<string> credits = WithOsmCredit(lines);
            if (_label != null)
            {
                _label.text = string.Join("  ", credits);
            }

            if (_panelText != null)
            {
                _panelText.text = "Map data\n\n" + string.Join("\n", credits)
                                  + "\n\nOpenStreetMap data is available under the Open Database License (ODbL)."
                                  + "\nTiles: OpenFreeMap (OpenMapTiles schema)."
                                  + "\n\nPrivacy: the tile host sees which map area (~1 km) you load and your IP address.";
            }
        }

        /// <summary>OSMF attribution guidelines ask for "© OpenStreetMap"; add it when the source text lacks it.</summary>
        public static List<string> WithOsmCredit(IReadOnlyList<string> lines)
        {
            var credits = new List<string>();
            bool hasOsm = false;
            foreach (string l in lines)
            {
                credits.Add(l);
                hasOsm |= l.IndexOf("© OpenStreetMap", System.StringComparison.Ordinal) >= 0;
            }

            if (!hasOsm)
            {
                credits.Add("© OpenStreetMap contributors");
            }

            return credits;
        }
    }
}
