using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Prototype AR encounter HUD (original procedural styling): player card top-left, circular radar
    /// top-right, quest tracker lower-left, circular action buttons right, bottom navigation bar,
    /// scanning/placement status and failure panel. The center of the screen stays clear.
    /// The first two actions are Attack and Aura; other buttons show a prototype notice.
    /// </summary>
    public sealed class ArEncounterHud : MonoBehaviour
    {
        public const string UnassignedNotice = "Funkcja jeszcze nieprzypisana (prototyp)";

        [SerializeField]
        private TextMeshProUGUI _statusText;

        [SerializeField]
        private GameObject _statusRoot;

        [SerializeField]
        private Button _placeButton;

        [SerializeField]
        private Button _exitButton;

        [SerializeField]
        private GameObject _failurePanel;

        [SerializeField]
        private TextMeshProUGUI _failureTitle;

        [SerializeField]
        private TextMeshProUGUI _failureText;

        [SerializeField]
        private Button _failureBackButton;

        [SerializeField]
        private Button[] _actionButtons;

        [SerializeField]
        private Button[] _navButtons;

        [SerializeField]
        private GameObject _toastRoot;

        [SerializeField]
        private TextMeshProUGUI _toastText;

        [SerializeField]
        private RectTransform _radarMobDot;

        [SerializeField]
        private float _radarRangeMeters = 10f;

        [SerializeField]
        private float _radarRadiusPixels = 85f;

        [SerializeField]
        private float _toastSeconds = 2.5f;

        private ArEncounterSceneController _controller;
        private Coroutine _toastRoutine;

        public Button PlaceButton => _placeButton;
        public Button ExitButton => _exitButton;
        public Button FailureBackButton => _failureBackButton;
        public Button[] ActionButtons => _actionButtons;
        public Button[] NavButtons => _navButtons;
        public string StatusLabel => _statusText != null ? _statusText.text : null;
        public bool IsStatusVisible => _statusRoot != null && _statusRoot.activeSelf;
        public bool IsPlaceButtonVisible => _placeButton != null && _placeButton.gameObject.activeSelf;
        public bool IsPlaceButtonInteractable => IsPlaceButtonVisible && _placeButton.interactable;
        public bool IsFailurePanelVisible => _failurePanel != null && _failurePanel.activeSelf;
        public string FailureTitle => _failureTitle != null ? _failureTitle.text : null;
        public string FailureLabel => _failureText != null ? _failureText.text : null;
        public bool IsToastVisible => _toastRoot != null && _toastRoot.activeSelf;
        public string LastToastMessage { get; private set; }
        public bool IsRadarMobDotVisible => _radarMobDot != null && _radarMobDot.gameObject.activeSelf;

        public void Configure(
            TextMeshProUGUI statusText,
            GameObject statusRoot,
            Button placeButton,
            Button exitButton,
            GameObject failurePanel,
            TextMeshProUGUI failureTitle,
            TextMeshProUGUI failureText,
            Button failureBackButton,
            Button[] actionButtons,
            Button[] navButtons,
            GameObject toastRoot,
            TextMeshProUGUI toastText,
            RectTransform radarMobDot)
        {
            _statusText = statusText;
            _statusRoot = statusRoot;
            _placeButton = placeButton;
            _exitButton = exitButton;
            _failurePanel = failurePanel;
            _failureTitle = failureTitle;
            _failureText = failureText;
            _failureBackButton = failureBackButton;
            _actionButtons = actionButtons;
            _navButtons = navButtons;
            _toastRoot = toastRoot;
            _toastText = toastText;
            _radarMobDot = radarMobDot;
        }

        /// <summary>Wires the buttons to the scene controller (single runtime path for touch and tests).</summary>
        public void Bind(ArEncounterSceneController controller)
        {
            _controller = controller;
            Wire(_placeButton, OnPlaceClicked);
            Wire(_exitButton, OnExitClicked);
            Wire(_failureBackButton, OnExitClicked);
            if (_actionButtons != null)
            {
                for (int i = 0; i < _actionButtons.Length; i++)
                {
                    Button b = _actionButtons[i];
                    if (b == null) continue;
                    b.onClick.RemoveListener(OnUnassignedClicked);
                    if (i == 0) Wire(b, OnAttackClicked);
                    else if (i == 1) Wire(b, OnAuraClicked);
                    else Wire(b, OnUnassignedClicked);
                    if (i < 2)
                    {
                        TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
                        if (label != null) { label.text = i == 0 ? "ATAK" : "AURA"; label.fontSize = 24; }
                        b.interactable = false;
                    }
                }
            }

            if (_navButtons != null)
            {
                foreach (Button b in _navButtons)
                {
                    Wire(b, OnUnassignedClicked);
                }
            }
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void OnPlaceClicked()
        {
            _controller?.ConfirmPlacement();
        }

        private void OnExitClicked()
        {
            _controller?.ExitToWorldMap();
        }

        private void OnUnassignedClicked()
        {
            ShowToast(UnassignedNotice);
        }

        private void OnAttackClicked() { _controller?.FppCombat?.RequestAttack(); }
        private void OnAuraClicked() { _controller?.FppCombat?.RequestAura(); }

        public void RefreshCombatButtons(bool ready, FppCombatTimeline timeline)
        {
            if (_actionButtons == null) return;
            bool idle = timeline != null && timeline.Action == FppCombatAction.Idle;
            if (_actionButtons.Length > 0 && _actionButtons[0] != null)
                _actionButtons[0].interactable = ready && idle;
            if (_actionButtons.Length > 1 && _actionButtons[1] != null)
                _actionButtons[1].interactable = ready && idle && !timeline.AuraActive;
        }

        /// <summary>Updates status / place button / failure panel / radar from the controller state.</summary>
        public void Refresh(ArEncounterState state, ArEncounterStateMachine machine, bool hasPlacementTarget, float notTrackingSeconds, string mobName, Transform mob, Transform viewer)
        {
            string status;
            switch (state)
            {
                case ArEncounterState.Starting:
                    status = "Uruchamianie AR...";
                    break;
                case ArEncounterState.Scanning:
                    status = "Skanuj teren: powoli przesuwaj telefon, celując w płaskie podłoże.";
                    if (notTrackingSeconds > 10f)
                    {
                        status += "\nBrak śledzenia — sprawdź zgodę na aparat i oświetlenie.";
                    }

                    break;
                case ArEncounterState.ReadyToPlace:
                    status = hasPlacementTarget
                        ? "Podłoże wykryte. Naciśnij \"Postaw potwora\"."
                        : "Podłoże wykryte. Wyceluj w podłoże.";
                    break;
                case ArEncounterState.Placed:
                    status = machine != null && machine.IsTrackingLimited
                        ? "Śledzenie ograniczone — skieruj aparat na miejsce potwora."
                        : mobName + " stoi na podłożu.";
                    break;
                default:
                    status = string.Empty;
                    break;
            }

            bool failed = state == ArEncounterState.Unsupported || state == ArEncounterState.Failed;
            if (_statusText != null)
            {
                _statusText.text = status;
            }

            if (_statusRoot != null)
            {
                _statusRoot.SetActive(!failed);
            }

            if (_placeButton != null)
            {
                bool show = state == ArEncounterState.Scanning || state == ArEncounterState.ReadyToPlace;
                _placeButton.gameObject.SetActive(show);
                _placeButton.interactable = state == ArEncounterState.ReadyToPlace && hasPlacementTarget;
            }

            if (_failurePanel != null)
            {
                _failurePanel.SetActive(failed);
                if (failed)
                {
                    if (_failureTitle != null)
                    {
                        _failureTitle.text = state == ArEncounterState.Unsupported ? "AR niedostępne" : "Błąd sesji AR";
                    }

                    if (_failureText != null)
                    {
                        _failureText.text = machine != null ? machine.FailureReason : string.Empty;
                    }
                }
            }

            UpdateRadar(mob, viewer);
        }

        private void UpdateRadar(Transform mob, Transform viewer)
        {
            if (_radarMobDot == null)
            {
                return;
            }

            if (mob == null || viewer == null)
            {
                _radarMobDot.gameObject.SetActive(false);
                return;
            }

            // Local AR offset (not GPS), rotated so "up" on the radar is the camera's forward direction.
            Vector3 offset = mob.position - viewer.position;
            Vector3 forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }

            Quaternion toView = Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up));
            Vector3 local = toView * new Vector3(offset.x, 0f, offset.z);
            Vector2 p = new Vector2(local.x, local.z) / Mathf.Max(0.01f, _radarRangeMeters) * _radarRadiusPixels;
            if (p.magnitude > _radarRadiusPixels)
            {
                p = p.normalized * _radarRadiusPixels;
            }

            _radarMobDot.anchoredPosition = p;
            _radarMobDot.gameObject.SetActive(true);
        }

        public void ShowToast(string message)
        {
            LastToastMessage = message;
            if (_toastText != null)
            {
                _toastText.text = message;
            }

            if (_toastRoot != null)
            {
                _toastRoot.SetActive(true);
            }

            if (_toastRoutine != null)
            {
                StopCoroutine(_toastRoutine);
            }

            if (isActiveAndEnabled)
            {
                _toastRoutine = StartCoroutine(HideToastAfterDelay());
            }
        }

        private IEnumerator HideToastAfterDelay()
        {
            yield return new WaitForSecondsRealtime(_toastSeconds);
            if (_toastRoot != null)
            {
                _toastRoot.SetActive(false);
            }

            _toastRoutine = null;
        }
    }
}

