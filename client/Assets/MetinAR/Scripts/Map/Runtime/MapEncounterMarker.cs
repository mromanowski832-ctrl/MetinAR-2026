using System;
using MetinAR.Location;
using MetinAR.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// METINAR-042 (#87) — NOT EDITOR-VERIFIED (written on the box without Unity; needs batch compile + Play Mode).
    /// One monster icon on the OSM map: label "Trujący Pająk (Poz. 5)\n50 m", the nearby notice
    /// "Trujący Pająk — 50 m", and the "Walcz" button that is interactable only when
    /// <see cref="EncounterEligibility"/> says the AR encounter is available (distance, fix accuracy, stable streak,
    /// safe site, player not on road/rail/water). Pressing it raises <see cref="EncounterRequested"/> with an
    /// <see cref="EncounterTicket"/>; wiring that event to <c>ArEncounterLauncher</c> in the game scene is the
    /// Developer's integration step (#87 subcontractor), not done here.
    /// The site is placed through <see cref="EncounterSitePolicy"/> (default-deny); with no safe site the marker hides.
    /// Logcat "[MapEncounter]" prints only distance/accuracy/state, never coordinates.
    /// </summary>
    public sealed class MapEncounterMarker : MonoBehaviour
    {
        [SerializeField] private PlayerLocationService _location;
        [SerializeField] private WorldMapOrigin _origin;
        [SerializeField] private string _mobId = "venomous_spider";
        [SerializeField] private string _mobName = "Trujący Pająk";
        [SerializeField] private int _level = 5;
        [SerializeField] private bool _aggressive = true;
        [SerializeField] private float _noticeRadiusMeters = 150f;
        [SerializeField] private TextMeshPro _label;
        [SerializeField] private TMP_Text _notice;
        [SerializeField] private Button _fightButton;
        [SerializeField] private TMP_Text _fightStatus;
        [SerializeField] private GameObject _iconRoot;
        [SerializeField] private float _groundY = 0.5f;

        private readonly EncounterEligibility _eligibility = new EncounterEligibility();
        private EncounterSitePolicy _policy;
        private GeoCoordinate _site;
        private bool _hasSite;
        private bool _siteSafe;
        private double _lastFixTimestamp = double.NaN;
        private double _lastFixReceivedAt;
        private EncounterAvailability _loggedState = EncounterAvailability.None;

        /// <summary>Raised when the player presses "Walcz" while the encounter is available.</summary>
        public event Action<EncounterTicket> EncounterRequested;

        public EncounterEligibility Eligibility => _eligibility;

        public bool HasSite => _hasSite;

        public string MobName => _mobName;

        public int Level => _level;

        /// <summary>Mock provider = developer simulation: rewards must be tagged and not saved as production.</summary>
        public LocationSourceKind Source => _location != null && _location.MockProvider != null
            ? LocationSourceKind.Simulation
            : LocationSourceKind.Gps;

        public void Configure(PlayerLocationService location, WorldMapOrigin origin, EncounterSitePolicy policy)
        {
            _location = location;
            _origin = origin;
            _policy = policy;
        }

        /// <summary>
        /// Places the mob near <paramref name="desired"/> through the safety policy. Returns false and hides the
        /// marker when no safe site exists (no data, road, rail, water, building, private/industrial).
        /// </summary>
        public bool TryPlace(GeoCoordinate desired, double searchRadiusMeters)
        {
            _hasSite = false;
            _siteSafe = false;
            _eligibility.Reset();
            if (_policy == null)
            {
                Debug.LogWarning("[MapEncounter] no safety policy: not spawning " + _mobId);
                SetVisible(false);
                return false;
            }

            if (!_policy.TryPlace(desired, searchRadiusMeters, out GeoCoordinate site, out EncounterSiteDecision first))
            {
                Debug.Log("[MapEncounter] " + _mobId + " not placed: " + first);
                SetVisible(false);
                return false;
            }

            _site = site;
            _hasSite = true;
            _siteSafe = true;
            SetVisible(true);
            return true;
        }

        private void Awake()
        {
            if (_fightButton != null)
            {
                _fightButton.onClick.AddListener(OnFightPressed);
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_fightButton != null)
            {
                _fightButton.onClick.RemoveListener(OnFightPressed);
            }
        }

        private void Update()
        {
            if (!_hasSite || _location == null || _origin == null || !_origin.HasOrigin)
            {
                SetButton(false, string.Empty);
                return;
            }

            transform.position = _origin.WorldToLocal(_site, _groundY);
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_location.TryGetLatestFix(out LocationFix fix))
            {
                SetButton(false, EncounterLabelFormatter.FormatAvailability(EncounterAvailability.StaleFix, 0));
                return;
            }

            if (fix.TimestampSeconds != _lastFixTimestamp)
            {
                _lastFixTimestamp = fix.TimestampSeconds;
                _lastFixReceivedAt = now;
            }

            double distance = GeoMath.DistanceMeters(fix.Coordinate, _site);
            bool playerOk = _policy == null || _policy.IsPlayerSpotAcceptable(fix.Coordinate, out _);
            EncounterAvailability state = _eligibility.Update(
                distance, fix.HorizontalAccuracyMeters, _lastFixReceivedAt, now, _siteSafe && playerOk);

            if (_label != null)
            {
                _label.text = EncounterLabelFormatter.FormatMarker(_mobName, _level, distance, _aggressive);
            }

            if (_notice != null)
            {
                bool near = distance <= _noticeRadiusMeters;
                _notice.gameObject.SetActive(near);
                if (near)
                {
                    _notice.text = EncounterLabelFormatter.FormatNotice(_mobName, distance);
                }
            }

            SetButton(state == EncounterAvailability.Available,
                EncounterLabelFormatter.FormatAvailability(state, _eligibility.DistanceToEnterMeters));

            if (state != _loggedState)
            {
                _loggedState = state;
                Debug.Log("[MapEncounter] " + _mobId + " state=" + state + " d=" + distance.ToString("0")
                          + " acc=" + fix.HorizontalAccuracyMeters.ToString("0") + " src=" + Source);
            }
        }

        private void OnFightPressed()
        {
            if (_eligibility.TryCreateTicket(_mobId, _mobName, _level, Source, out EncounterTicket ticket))
            {
                Debug.Log("[MapEncounter] request " + ticket);
                EncounterRequested?.Invoke(ticket);
            }
        }

        private void SetButton(bool interactable, string status)
        {
            if (_fightButton != null)
            {
                _fightButton.interactable = interactable;
            }

            if (_fightStatus != null)
            {
                _fightStatus.text = status;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_iconRoot != null)
            {
                _iconRoot.SetActive(visible);
            }

            if (_label != null)
            {
                _label.gameObject.SetActive(visible);
            }

            if (!visible && _notice != null)
            {
                _notice.gameObject.SetActive(false);
            }
        }
    }
}
