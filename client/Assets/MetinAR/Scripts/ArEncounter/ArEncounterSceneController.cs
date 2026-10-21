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

        /// <summary>Anchor the monster is parented to (null while pending or if anchoring was unavailable).</summary>
        public ARAnchor MobAnchor { get; private set; }

        /// <summary>
        /// How the monster is held in place: "world" (free ARAnchor at a fixed world pose, #31 A31 retest 2026-10-21),
        /// "plane" (fallback: anchor attached to the detected plane), "session" (no anchor: fixed in session space) or "pending".
        /// </summary>
        public string MobAnchorKind { get; private set; } = "none";

        /// <summary>Seconds between [ArAnchor] diagnostic log lines while a monster is placed (0 = off).</summary>
        public float AnchorDiagnosticsIntervalSeconds { get; set; } = 2f;

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

        /// <summary>METINAR-020: hit → damage → mob HP → death → EXP/talary (added at runtime in Awake).</summary>
        public ArEncounterCombat Combat { get; private set; }

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
            Combat = GetComponent<ArEncounterCombat>();
            if (Combat == null)
            {
                Combat = gameObject.AddComponent<ArEncounterCombat>();
            }

            Combat.Initialize(this);
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
            // METINAR-020 (S2a) x #31: a geometric FPP sword hit on the placed mob becomes damage.
            FppCombat.HitDetected += Combat.HandleHitDetected;

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

            if (FppCombat != null && Combat != null)
            {
                FppCombat.HitDetected -= Combat.HandleHitDetected;
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
                UpdateMobTrackingVisibility();
                LogAnchorDiagnostics();
            }

            RefreshHud();
        }

        /// <summary>
        /// #31 v0 A31 P0-2: when the AR session stops tracking (VIO reset), the camera pose freezes while the camera image
        /// keeps moving, so a drawn mob looks glued to the phone. Hide it until tracking returns; its world pose is untouched.
        /// </summary>
        private void UpdateMobTrackingVisibility()
        {
            if (SpawnedMob == null)
            {
                return;
            }

            bool visible = LastSessionStatus == ArSessionStatus.SessionTracking;
            if (visible != SpawnedMob.IsTrackingVisible)
            {
                SpawnedMob.SetTrackingVisible(visible);
                Debug.Log("[ArEncounter] mob " + (visible ? "shown (tracking)" : "hidden (tracking lost: " + LastSessionStatus + ")"));
            }
        }

        private float _nextAnchorLog;
        private ArSessionStatus _anchorLogSession;
        private TrackingState _anchorLogAnchorState = TrackingState.None;
        private string _anchorLogKind;

        /// <summary>v0 acceptance probe for "the spider moves together with the phone" (#31); baseline = placement.</summary>
        public ArAnchorProbe AnchorProbe { get; } = new ArAnchorProbe();

        /// <summary>
        /// #31 A31 retest: "the spider moves forward together with the phone" (open v0 acceptance bug). Every
        /// <see cref="AnchorDiagnosticsIntervalSeconds"/> and immediately on any session-tracking, anchor-tracking or
        /// anchor-kind change, logs one <c>[ArAnchor]</c> line: anchor kind and state, session state, camera position and
        /// yaw/pitch, monster world position, anchor and XR origin positions, plus the <see cref="ArAnchorProbe"/> sample
        /// against the placement baseline (rotation, sidestep, closer/farther, monster drift and a STAYS / DRIFT /
        /// FOLLOWS_CAMERA verdict). The A31 test reads these lines for rotate, sidestep, approach, retreat and
        /// tracking loss/recovery.
        /// </summary>
        private void LogAnchorDiagnostics()
        {
            if (AnchorDiagnosticsIntervalSeconds <= 0f || SpawnedMob == null || _arCamera == null)
            {
                return;
            }

            TrackingState anchorState = MobAnchor != null ? MobAnchor.trackingState : TrackingState.None;
            string reason = null;
            if (LastSessionStatus != _anchorLogSession)
            {
                reason = "session " + _anchorLogSession + "->" + LastSessionStatus;
            }
            else if (anchorState != _anchorLogAnchorState)
            {
                reason = "anchorState " + _anchorLogAnchorState + "->" + anchorState;
            }
            else if (MobAnchorKind != _anchorLogKind)
            {
                reason = "kind " + _anchorLogKind + "->" + MobAnchorKind;
            }
            else if (Time.unscaledTime < _nextAnchorLog)
            {
                return;
            }

            _anchorLogSession = LastSessionStatus;
            _anchorLogAnchorState = anchorState;
            _anchorLogKind = MobAnchorKind;
            _nextAnchorLog = Time.unscaledTime + AnchorDiagnosticsIntervalSeconds;

            Transform camT = _arCamera.transform;
            Vector3 cam = camT.position;
            Vector3 euler = camT.rotation.eulerAngles;
            Vector3 mob = SpawnedMob.transform.position;
            if (!AnchorProbe.HasBaseline)
            {
                AnchorProbe.SetBaseline(cam.x, cam.z, euler.y, mob.x, mob.y, mob.z);
            }

            ArAnchorSample sample = AnchorProbe.Sample(cam.x, cam.z, euler.y, mob.x, mob.y, mob.z);
            Debug.Log("[ArAnchor] " + (reason != null ? "event=" + reason + " " : "tick ")
                      + "kind=" + MobAnchorKind
                      + " anchorState=" + anchorState
                      + " session=" + LastSessionStatus
                      + " cam=" + cam.ToString("F2") + " camYaw=" + euler.y.ToString("F0") + " camPitch=" + ArAnchorProbe.WrapDegrees(euler.x).ToString("F0")
                      + " mob=" + mob.ToString("F2")
                      + " anchor=" + (MobAnchor != null ? MobAnchor.transform.position.ToString("F2") : "-")
                      + " origin=" + (_origin != null ? _origin.transform.position.ToString("F2") : "-")
                      + " parent=" + (SpawnedMob.transform.parent != null ? SpawnedMob.transform.parent.name : "-")
                      + " " + sample
                      + " maxDrift=" + AnchorProbe.MaxDriftMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " followsCam=" + AnchorProbe.FollowsCameraSamples + "/" + AnchorProbe.Samples);
        }

        /// <summary>Restarts the anchor probe at the monster's current pose (placement, or once the world anchor is set).</summary>
        private void ResetAnchorProbe()
        {
            if (SpawnedMob == null || _arCamera == null)
            {
                return;
            }

            Transform camT = _arCamera.transform;
            Vector3 mob = SpawnedMob.transform.position;
            AnchorProbe.SetBaseline(camT.position.x, camT.position.z, camT.rotation.eulerAngles.y, mob.x, mob.y, mob.z);
            _anchorLogKind = null;
            _nextAnchorLog = 0f;
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

            // #31 A31 retest 2026-10-21 (spider moved with the phone): keep the monster at one fixed WORLD spot.
            // It is never parented to the camera. First it sits in session space (TrackablesParent); a free ARAnchor
            // at that world pose is then requested and the monster is re-parented to it. Plane-attached anchor and
            // session space remain as fallbacks.
            if (_origin != null && _origin.TrackablesParent != null)
            {
                go.transform.SetParent(_origin.TrackablesParent, true);
            }

            MobAnchorKind = "session";
            if (_anchorManager != null && _anchorManager.enabled && _anchorManager.subsystem != null)
            {
                MobAnchorKind = "pending";
                AnchorMobAtWorldPose(go.transform, pose, plane);
            }

            if (_placementReticle != null)
            {
                _placementReticle.gameObject.SetActive(false);
            }

            ResetAnchorProbe();
            Debug.Log("[ArEncounter] spawned " + Request.MobName + " at " + pose.position + " anchor=" + MobAnchorKind
                      + " scale=" + ArEncounterFraming.MobVisualScale);
            MobPlaced?.Invoke(SpawnedMob);
        }

        private async void AnchorMobAtWorldPose(Transform mob, Pose pose, ARPlane plane)
        {
            ARAnchor anchor = null;
            string kind = "session";
            try
            {
                Result<ARAnchor> result = await _anchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess() && result.value != null)
                {
                    anchor = result.value;
                    kind = "world";
                }
                else
                {
                    Debug.LogWarning("[ArEncounter] world anchor failed: " + result.status);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArEncounter] world anchor failed: " + ex.Message);
            }

            if (this == null || mob == null || IsExiting)
            {
                if (anchor != null && _anchorManager != null)
                {
                    _anchorManager.TryRemoveAnchor(anchor);
                }

                return;
            }

            if (anchor == null && plane != null)
            {
                try
                {
                    anchor = _anchorManager.AttachAnchor(plane, pose);
                    kind = anchor != null ? "plane" : kind;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ArEncounter] AttachAnchor failed: " + ex.Message);
                }
            }

            MobAnchor = anchor;
            MobAnchorKind = kind;
            if (anchor != null)
            {
                mob.SetParent(anchor.transform, true);
            }

            Debug.Log("[ArEncounter] anchor=" + kind + (anchor != null ? " at " + anchor.transform.position.ToString("F2") : string.Empty));
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

