using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// AR encounter prototype: scan → detect horizontal ground → place ONE stationary test monster on the
    /// real ground (anchored), with unsupported / failure states and a path back to the world map.
    /// The pure decision logic lives in <see cref="ArEncounterStateMachine"/>; this adapter feeds it
    /// AR Foundation data each frame. GPS is not used inside the encounter.
    /// Placement interaction (centre reticle + "Postaw potwora" confirm button) is a PROTOTYPE, not final design.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ArEncounterSceneController : MonoBehaviour
    {
        // Centre first, then points above it (farther ground) and below it. Hits closer than
        // ArEncounterFraming.MinPlacementDistanceMeters are skipped (#31 A31 retest: spider filled the screen).
        private static readonly Vector2[] ReticleViewportPoints =
        {
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.6f),
            new Vector2(0.5f, 0.7f),
            new Vector2(0.5f, 0.4f),
            new Vector2(0.5f, 0.3f),
        };

        [SerializeField]
        private ARSession _session;

        [SerializeField]
        private XROrigin _origin;

        [SerializeField]
        private ARPlaneManager _planeManager;

        [SerializeField]
        private ARRaycastManager _raycastManager;

        [SerializeField]
        private ARAnchorManager _anchorManager;

        [SerializeField]
        private Camera _arCamera;

        [SerializeField]
        private ArEncounterHud _hud;

        [SerializeField]
        private GameObject _mobPrefab;

        [SerializeField]
        private Transform _placementReticle;

        [SerializeField]
        private ArPlacementSettings _placementSettings = ArPlacementSettings.CreatePrototypeDefaults();

        [SerializeField]
        private string _worldMapScenePath = ArEncounterScenes.WorldMapScenePath;

        private readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        private readonly ArFrameSnapshot _frame = new ArFrameSnapshot();
        private readonly List<ArEncounterState> _stateHistory = new List<ArEncounterState>();
        private ArEncounterStateMachine _machine;
        private float _notTrackingSeconds;
        private bool _availabilityCheckStarted;

        /// <summary>Raised on every encounter state change (new state).</summary>
        public event Action<ArEncounterState> StateChanged;

        /// <summary>Raised once when the test monster is spawned.</summary>
        public event Action<ArEncounterMob> MobPlaced;

        /// <summary>
        /// Test / diagnostics seam: when set, it fills the frame snapshot instead of AR Foundation
        /// (same downstream path: state machine, HUD, placement, spawn).
        /// </summary>
        public Action<ArFrameSnapshot> FrameSourceOverride { get; set; }

        public ArEncounterRequest Request { get; private set; }

        /// <summary>True when the request came from the world map (false: scene opened directly, prototype test mob).</summary>
        public bool RequestFromWorldMap { get; private set; }

        public ArEncounterStateMachine Machine => _machine;

        public ArEncounterState State => _machine != null ? _machine.State : ArEncounterState.Starting;

        public IReadOnlyList<ArEncounterState> StateHistory => _stateHistory;

        public ArEncounterHud Hud => _hud;

        public ARPlaneManager PlaneManager => _planeManager;

        public ARSession Session => _session;

        public XROrigin Origin => _origin;

        public Camera ArCamera => _arCamera;

        public Transform PlacementReticle => _placementReticle;

        /// <summary>Number of monsters actually instantiated (must never exceed 1).</summary>
        public int SpawnCount { get; private set; }

        public ArEncounterMob SpawnedMob { get; private set; }

        /// <summary>Anchor the monster is parented to (null if anchoring was unavailable).</summary>
        public ARAnchor MobAnchor { get; private set; }

        public ArPlacementResult? LastPlacementResult { get; private set; }

        public bool HasPlacementTarget { get; private set; }

        public Pose PlacementTargetPose { get; private set; }

        public string PlacementTargetPlaneId { get; private set; }

        public bool PlacementTargetFromReticle { get; private set; }

        /// <summary>Ground was found but every candidate target is closer than the minimum placement distance.</summary>
        public bool PlacementTargetTooClose { get; private set; }

        /// <summary>Latest session status fed to the state machine.</summary>
        public ArSessionStatus LastSessionStatus { get; private set; }

        public bool IsExiting { get; private set; }

        public FppCombatController FppCombat { get; private set; }

        public void Configure(
            ARSession session,
            XROrigin origin,
            ARPlaneManager planeManager,
            ARRaycastManager raycastManager,
            ARAnchorManager anchorManager,
            Camera arCamera,
            ArEncounterHud hud,
            GameObject mobPrefab,
            Transform placementReticle)
        {
            _session = session;
            _origin = origin;
            _planeManager = planeManager;
            _raycastManager = raycastManager;
            _anchorManager = anchorManager;
            _arCamera = arCamera;
            _hud = hud;
            _mobPrefab = mobPrefab;
            _placementReticle = placementReticle;
        }

        private void Awake()
        {
            _machine = new ArEncounterStateMachine(_placementSettings);
            _stateHistory.Add(_machine.State);
            _machine.StateChanged += OnMachineStateChanged;

            if (ArEncounterHandoff.TryConsume(out ArEncounterRequest request))
            {
                Request = request;
                RequestFromWorldMap = true;
            }
            else
            {
                Request = ArEncounterRequest.PrototypeTestSpider("direct");
                RequestFromWorldMap = false;
            }

            Debug.Log("[ArEncounter] request " + Request + (RequestFromWorldMap ? " (world map)" : " (direct open)"));
            if (_hud != null)
            {
                _hud.Bind(this);
            }

            if (_placementReticle != null)
            {
                _placementReticle.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            // Owner-approved FPP prototype (#31). Main has no live-hand inference dependency.
            FppCombat = gameObject.AddComponent<FppCombatController>();
            FppCombat.Initialize(this);

            if (!_availabilityCheckStarted && FrameSourceOverride == null)
            {
                // Resolves Unsupported when no AR subsystem exists (ARSession itself only checks when it has one).
                _availabilityCheckStarted = true;
                StartCoroutine(ARSession.CheckAvailability());
            }

            RefreshHud();
        }

        private void OnDestroy()
        {
            if (_machine != null)
            {
                _machine.StateChanged -= OnMachineStateChanged;
            }
        }

        private void OnMachineStateChanged(ArEncounterState previous, ArEncounterState next)
        {
            _stateHistory.Add(next);
            Debug.Log("[ArEncounter] state " + previous + " -> " + next
                      + (string.IsNullOrEmpty(_machine.FailureReason) ? string.Empty : " (" + _machine.FailureReason + ")"));
            StateChanged?.Invoke(next);
        }

        private void Update()
        {
            if (_machine == null || IsExiting)
            {
                return;
            }

            _frame.Clear();
            if (FrameSourceOverride != null)
            {
                FrameSourceOverride(_frame);
            }
            else
            {
                CaptureFrame(_frame);
            }

            LastSessionStatus = _frame.SessionStatus;
            _machine.SetSessionStatus(_frame.SessionStatus);
            _machine.Tick(Time.unscaledDeltaTime);
            _machine.UpdatePlanes(_frame.Planes);

            _notTrackingSeconds = _frame.SessionStatus == ArSessionStatus.SessionInitializing
                ? _notTrackingSeconds + Time.unscaledDeltaTime
                : 0f;

            bool candidate = _machine.State == ArEncounterState.ReadyToPlace
                             && _frame.HasTarget
                             && _frame.TargetPlaneId != null
                             && ContainsSuitable(_frame.TargetPlaneId);
            bool farEnough = candidate && IsFarEnough(_frame.TargetPose.position);
            PlacementTargetTooClose = _machine.State == ArEncounterState.ReadyToPlace
                                      && ((candidate && !farEnough) || (!_frame.HasTarget && _frame.TargetTooClose));
            HasPlacementTarget = candidate && farEnough;
            if (HasPlacementTarget)
            {
                PlacementTargetPose = _frame.TargetPose;
                PlacementTargetPlaneId = _frame.TargetPlaneId;
                PlacementTargetFromReticle = _frame.TargetFromReticle;
                _targetPlane = _frame.TargetPlane;
            }
            else
            {
                PlacementTargetPlaneId = null;
                _targetPlane = null;
            }

            if (_placementReticle != null)
            {
                _placementReticle.gameObject.SetActive(HasPlacementTarget);
                if (HasPlacementTarget)
                {
                    _placementReticle.SetPositionAndRotation(PlacementTargetPose.position + Vector3.up * 0.003f, PlacementTargetPose.rotation);
                }
            }

            if (_machine.State == ArEncounterState.Placed)
            {
                HidePlaneVisuals();
            }

            RefreshHud();
        }

        private ARPlane _targetPlane;

        private bool ContainsSuitable(string planeId)
        {
            foreach (string id in _machine.SuitablePlaneIds)
            {
                if (id == planeId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshHud()
        {
            if (_hud == null || _machine == null)
            {
                return;
            }

            _hud.Refresh(
                _machine.State,
                _machine,
                HasPlacementTarget,
                _notTrackingSeconds,
                Request != null ? Request.MobName : string.Empty,
                SpawnedMob != null ? SpawnedMob.transform : null,
                _arCamera != null ? _arCamera.transform : null,
                PlacementTargetTooClose);
        }

        /// <summary>Placement target at least the minimum ground distance from the AR camera (true without a camera).</summary>
        public bool IsFarEnough(Vector3 target)
        {
            if (_arCamera == null)
            {
                return true;
            }

            Vector3 c = _arCamera.transform.position;
            return ArEncounterFraming.IsPlacementFarEnough(c.x, c.z, target.x, target.z);
        }

        /// <summary>Maps AR Foundation's session state to the project-owned mirror.</summary>
        public static ArSessionStatus Map(ARSessionState state)
        {
            switch (state)
            {
                case ARSessionState.Unsupported: return ArSessionStatus.Unsupported;
                case ARSessionState.CheckingAvailability: return ArSessionStatus.CheckingAvailability;
                case ARSessionState.NeedsInstall: return ArSessionStatus.NeedsInstall;
                case ARSessionState.Installing: return ArSessionStatus.Installing;
                case ARSessionState.Ready: return ArSessionStatus.Ready;
                case ARSessionState.SessionInitializing: return ArSessionStatus.SessionInitializing;
                case ARSessionState.SessionTracking: return ArSessionStatus.SessionTracking;
                default: return ArSessionStatus.None;
            }
        }

        /// <summary>Fills <paramref name="frame"/> from AR Foundation (session state, planes, reticle raycast).</summary>
        private void CaptureFrame(ArFrameSnapshot frame)
        {
            frame.SessionStatus = Map(ARSession.state);
            if (_planeManager == null)
            {
                return;
            }

            ARPlane nearestSuitable = null;
            float nearestDistance = float.PositiveInfinity;
            Vector3 cameraPosition = _arCamera != null ? _arCamera.transform.position : Vector3.zero;
            foreach (ARPlane plane in _planeManager.trackables)
            {
                var observation = new ArPlaneObservation(
                    plane.trackableId.ToString(),
                    plane.alignment == PlaneAlignment.HorizontalUp,
                    plane.trackingState == TrackingState.Tracking,
                    plane.size.x,
                    plane.size.y,
                    plane.subsumedBy != null);
                frame.Planes.Add(observation);
                if (_machine.IsSuitable(observation))
                {
                    float d = Vector3.Distance(cameraPosition, plane.center);
                    if (d < nearestDistance)
                    {
                        nearestDistance = d;
                        nearestSuitable = plane;
                    }
                }
            }

            if (nearestSuitable == null || _arCamera == null)
            {
                return;
            }

            // Prefer the screen-centre reticle (then slightly lower points) on a suitable horizontal plane.
            if (_raycastManager != null && _raycastManager.enabled)
            {
                foreach (Vector2 viewport in ReticleViewportPoints)
                {
                    Vector2 screen = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
                    if (!_raycastManager.Raycast(screen, _hits, TrackableType.PlaneWithinPolygon))
                    {
                        continue;
                    }

                    foreach (ARRaycastHit hit in _hits)
                    {
                        ARPlane plane = _planeManager.GetPlane(hit.trackableId);
                        if (plane == null || !IsSuitablePlane(plane))
                        {
                            continue;
                        }

                        if (!IsFarEnough(hit.pose.position))
                        {
                            frame.TargetTooClose = true;
                            continue;
                        }

                        SetTarget(frame, plane, hit.pose.position, true);
                        return;
                    }
                }
            }

            // Fallback: centre of the nearest suitable plane (still real detected ground), if it is far enough.
            if (!IsFarEnough(nearestSuitable.center))
            {
                frame.TargetTooClose = true;
                return;
            }

            SetTarget(frame, nearestSuitable, nearestSuitable.center, false);
        }

        private bool IsSuitablePlane(ARPlane plane)
        {
            return _machine.IsSuitable(new ArPlaneObservation(
                plane.trackableId.ToString(),
                plane.alignment == PlaneAlignment.HorizontalUp,
                plane.trackingState == TrackingState.Tracking,
                plane.size.x,
                plane.size.y,
                plane.subsumedBy != null));
        }

        private void SetTarget(ArFrameSnapshot frame, ARPlane plane, Vector3 position, bool fromReticle)
        {
            frame.HasTarget = true;
            frame.TargetPlane = plane;
            frame.TargetPlaneId = plane.trackableId.ToString();
            frame.TargetPose = new Pose(position, FacingCamera(position));
            frame.TargetFromReticle = fromReticle;
        }

        /// <summary>Yaw-only rotation so the monster faces the AR camera.</summary>
        public Quaternion FacingCamera(Vector3 position)
        {
            if (_arCamera == null)
            {
                return Quaternion.identity;
            }

            Vector3 toCamera = Vector3.ProjectOnPlane(_arCamera.transform.position - position, Vector3.up);
            return toCamera.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toCamera.normalized, Vector3.up) : Quaternion.identity;
        }

        /// <summary>
        /// "Postaw potwora" confirm (PROTOTYPE interaction). Spawns the single test monster at the current
        /// placement target when ready; repeated calls never spawn a duplicate.
        /// </summary>
        public ArPlacementResult ConfirmPlacement()
        {
            if (_machine == null)
            {
                return ArPlacementResult.NotReady;
            }

            ArPlacementResult result;
            if (_machine.State == ArEncounterState.ReadyToPlace && !HasPlacementTarget)
            {
                result = ArPlacementResult.UnsuitablePlane;
            }
            else
            {
                result = _machine.TryPlace(PlacementTargetPlaneId);
            }

            LastPlacementResult = result;
            if (result == ArPlacementResult.Placed)
            {
                Spawn(PlacementTargetPose, _targetPlane);
            }

            Debug.Log("[ArEncounter] placement " + result + " spawns=" + SpawnCount);
            RefreshHud();
            return result;
        }

        private void Spawn(Pose pose, ARPlane plane)
        {
            if (SpawnCount > 0 || _mobPrefab == null)
            {
                return;
            }

            GameObject go = Instantiate(_mobPrefab, pose.position, pose.rotation);
            go.name = _mobPrefab.name;
            // Visual size only (#31 A31 retest); level/HP/aggro come from the request and are unchanged.
            go.transform.localScale = _mobPrefab.transform.localScale * ArEncounterFraming.MobVisualScale;
            SpawnCount++;
            SpawnedMob = go.GetComponent<ArEncounterMob>();
            if (SpawnedMob == null)
            {
                SpawnedMob = go.AddComponent<ArEncounterMob>();
            }

            SpawnedMob.Configure(Request, _arCamera);

            // Anchor to the detected plane so AR tracking keeps the monster in place.
            if (plane != null && _anchorManager != null && _anchorManager.enabled && _anchorManager.subsystem != null)
            {
                try
                {
                    MobAnchor = _anchorManager.AttachAnchor(plane, pose);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ArEncounter] AttachAnchor failed: " + ex.Message);
                    MobAnchor = null;
                }
            }

            if (MobAnchor != null)
            {
                go.transform.SetParent(MobAnchor.transform, true);
            }
            else if (_origin != null && _origin.TrackablesParent != null)
            {
                // No anchor available: keep it in session space (fixed relative to the AR session origin).
                go.transform.SetParent(_origin.TrackablesParent, true);
            }

            if (_placementReticle != null)
            {
                _placementReticle.gameObject.SetActive(false);
            }

            Debug.Log("[ArEncounter] spawned " + Request.MobName + " at " + pose.position + " anchored=" + (MobAnchor != null));
            MobPlaced?.Invoke(SpawnedMob);
        }

        private void HidePlaneVisuals()
        {
            if (_planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (plane.gameObject.activeSelf)
                {
                    plane.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Leaves the encounter and returns to the world-map prototype scene.</summary>
        public void ExitToWorldMap()
        {
            if (IsExiting)
            {
                return;
            }

            IsExiting = true;
            FppCombat?.CancelActions();
            ArEncounterHandoff.Clear();
            Debug.Log("[ArEncounter] exit to world map");
            if (_session != null && _session.subsystem != null)
            {
                // Next encounter starts with fresh planes/anchors.
                _session.Reset();
            }

            SceneManager.LoadScene(_worldMapScenePath);
        }
    }
}

