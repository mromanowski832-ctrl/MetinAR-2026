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
    /// AR encounter prototype: scan → detect horizontal ground → place ONE test monster on the real ground (anchored),
    /// with unsupported / failure states and a path back to the world map. #97: the player never walks in AR — the mob
    /// comes to the camera by itself (<see cref="ArNoWalkEncounter"/>), stops at the blade-derived stand-off distance and
    /// is restored camera-relative after a tracking reset.
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

        /// <summary>#97 PROTOTYPE values of the no-walk AR combat (stand-off margin, d_min, speeds, step limit).</summary>
        [SerializeField]
        private ArNoWalkSettings _noWalkSettings = new ArNoWalkSettings();

        /// <summary>Visual scale of the spawned mob, from <see cref="ArNoWalkSettings.mobVisualScale"/> (#97, lead 04:40: default 0.9).</summary>
        public float MobVisualScale => (float)(_noWalkSettings != null ? _noWalkSettings.mobVisualScale : ArEncounterFraming.DefaultMobVisualScale);

        [SerializeField]
        private string _worldMapScenePath = ArEncounterScenes.WorldMapScenePath;

        private readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        private readonly ArFrameSnapshot _frame = new ArFrameSnapshot();
        private readonly List<ArEncounterState> _stateHistory = new List<ArEncounterState>();
        private ArEncounterStateMachine _machine;
        private float _notTrackingSeconds;
        private bool _availabilityCheckStarted;
        private ArTrackingRecovery _recovery;
        private ArNoWalkEncounter _noWalk;
        private ArMobMotion _loggedMotion = ArMobMotion.Holding;
        private bool _loggedInReach;
        private int _anchorGeneration;
        private string _loggedTargetPlane;
        private bool _loggedTargetAccepted;

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

        /// <summary>#31 MANAGER DECISION 18:50: hide + pause on tracking loss, ARSession reset after the prototype timeout.</summary>
        public ArTrackingRecovery TrackingRecovery => _recovery;

        /// <summary>The placed mob may be drawn and fought (tracking, and not waiting for a new anchor after a session reset).</summary>
        public bool MobUsable => _recovery == null || _recovery.MobUsable;

        /// <summary>The AR session was reset and the mob still waits for new ground to be anchored on.</summary>
        public bool IsRelocatingMob => _recovery != null && _recovery.AwaitingRelocation;

        /// <summary>Ground was found but the target surface is too far below the camera (#31 P1).</summary>
        public bool PlacementTargetTooLow { get; private set; }

        /// <summary>Ground was found but only beyond the v0 maximum spawn distance (#31 decision 18:58).</summary>
        public bool PlacementTargetTooFar { get; private set; }

        /// <summary>Horizontal camera-to-mob distance of the placed mob (NaN before placement).</summary>
        public float ApproachDistance { get; private set; } = float.NaN;

        /// <summary>
        /// Distance-only attack reach (#31 18:58 correction): horizontal camera-to-hit-footprint distance ≤ blade reach.
        /// Independent of where the phone aims; the aimed blade sweep is used for hits only.
        /// </summary>
        public bool InAttackReach { get; private set; }

        /// <summary>Horizontal distance from the camera to the mob's hit footprint (NaN before placement).</summary>
        public float FootprintDistance { get; private set; } = float.NaN;

        /// <summary>#97: no-walk combat logic of the placed mob (approach, pause on loss, camera-relative restore).</summary>
        public ArNoWalkEncounter NoWalk => _noWalk;

        /// <summary>#97 d_stop: stand-off distance derived from the blade geometry (R_h − m + f, never below d_min).</summary>
        public float MobStopDistance => _noWalk != null ? (float)_noWalk.Approach.StopDistanceMeters : float.NaN;

        /// <summary>What the placed mob is doing on its own (#97).</summary>
        public ArMobMotion MobMotion { get; private set; } = ArMobMotion.Holding;

        /// <summary>Horizontal distance the mob moved by itself in the last frame (#97 no-teleport check).</summary>
        public float LastMobStepMeters { get; private set; }

        /// <summary>Largest single-frame self-move of the mob so far, including camera-relative restores (#97 no-teleport check).</summary>
        public float MaxMobStepMeters { get; private set; }

        /// <summary>Restarts the <see cref="MaxMobStepMeters"/> statistic (smoke tests measure one scenario at a time).</summary>
        public void ResetMobStepStats()
        {
            LastMobStepMeters = 0f;
            MaxMobStepMeters = 0f;
        }

#if UNITY_EDITOR
        /// <summary>The approach can be switched off only in the Editor (smoke-test seam), never in a player build.</summary>
        public const bool MobApproachTestSeamAvailable = true;
#else
        public const bool MobApproachTestSeamAvailable = false;
#endif

        private bool _mobApproachTestRequest = true;

        /// <summary>
        /// Lets the mob walk to the camera (#97, always on in game builds — lead decision 04:42, Q9). Not serialized, not
        /// settable: only Editor smoke tests that hold the mob at a fixed pose can turn it off via
        /// <see cref="SetMobApproachEnabledForTests"/>; outside the Editor the request is ignored (<see cref="ArMobApproachGate"/>).
        /// </summary>
        public bool MobApproachEnabled => ArMobApproachGate.Effective(_mobApproachTestRequest, MobApproachTestSeamAvailable);

        /// <summary>Editor smoke-test seam (#97 Q9). In a player build a "false" is ignored and logged.</summary>
        public void SetMobApproachEnabledForTests(bool enabled)
        {
            _mobApproachTestRequest = enabled;
            if (!enabled && !MobApproachTestSeamAvailable)
            {
                Debug.LogWarning("[ArEncounter] mob approach cannot be disabled outside the Editor; request ignored");
            }
        }

        /// <summary>
        /// Swings may start: tracking, no pending restore, and — after any loss — tracking stable for
        /// <see cref="ArNoWalkSettings.combatResumeStableSeconds"/> (#97 Q2, lead 04:42).
        /// </summary>
        public bool CombatAllowed { get; private set; } = true;

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
            _recovery = new ArTrackingRecovery(_machine.Settings.trackingLostResetSeconds);
            _noWalkSettings = _noWalkSettings ?? new ArNoWalkSettings();
            double stop = ArMobStandoff.StopDistance(FppCombatController.BladeReachMeters, _noWalkSettings, out bool stopClamped);
            _noWalk = new ArNoWalkEncounter(_noWalkSettings, _recovery, stop);
            string standoff = "[ArEncounter] no-walk standoff d_stop=" + stop.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " bladeReach=" + FppCombatController.BladeReachMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " margin=" + _noWalkSettings.reachMarginMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " front=" + ArMobStandoff.HitFrontOffsetMeters(_noWalkSettings.mobVisualScale).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " d_min=" + _noWalkSettings.minDistanceMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " approachFrom=" + _noWalk.Approach.ApproachStartMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " maxStep=" + _noWalkSettings.MaxStepMeters.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (stopClamped)
            {
                Debug.LogWarning(standoff + " CLAMPED to d_min (blade reach too short for the margin)");
            }
            else
            {
                Debug.Log(standoff);
            }
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
            bool nearEnough = candidate && IsNearEnough(_frame.TargetPose.position);
            bool heightOk = candidate && IsPlaneHeightAcceptable(_frame.TargetPose.position);
            if (candidate)
            {
                LogTargetPlaneHeight(_frame.TargetPlaneId, _frame.TargetPose.position, farEnough && nearEnough && heightOk);
            }

            bool ready = _machine.State == ArEncounterState.ReadyToPlace;
            PlacementTargetTooClose = ready && ((candidate && !farEnough) || (!_frame.HasTarget && _frame.TargetTooClose));
            PlacementTargetTooFar = ready && ((candidate && farEnough && !nearEnough) || (!_frame.HasTarget && !_frame.TargetTooClose && _frame.TargetTooFar));
            PlacementTargetTooLow = ready && ((candidate && farEnough && nearEnough && !heightOk)
                                              || (!_frame.HasTarget && !_frame.TargetTooClose && !_frame.TargetTooFar && _frame.TargetTooLow));
            HasPlacementTarget = candidate && farEnough && nearEnough && heightOk;
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
                UpdateNoWalk();
                UpdateReachDiagnostics();
                LogAnchorDiagnostics();
            }

            RefreshHud();
        }

        /// <summary>
        /// #31 v0 A31 P0-2 + MANAGER DECISION 18:50, #97. When the AR session stops tracking (VIO reset) the camera pose
        /// freezes while the camera image keeps moving, so a drawn mob looks glued to the phone. The mob is hidden, its
        /// movement and combat are paused at once; after <see cref="ArPlacementSettings.trackingLostResetSeconds"/>
        /// (prototype 15 s) the ARSession is reset. The mob object and its fight (HP, once-only reward) survive; when
        /// tracking is back it is restored at the same camera-relative distance and bearing (#97, no reticle re-placement).
        /// While usable, the mob walks to / backs off to the stand-off distance by itself (#97, no player walking).
        /// </summary>
        private void UpdateNoWalk()
        {
            if (SpawnedMob == null || _noWalk == null)
            {
                return;
            }

            bool tracking = LastSessionStatus == ArSessionStatus.SessionTracking;
            Transform mobT = SpawnedMob.transform;
            ArNoWalkFrame frame;
            if (_arCamera == null)
            {
                // No camera: keep the #31 loss/reset behaviour, the mob cannot know where to walk.
                ArRecoveryAction action = _recovery.Tick(true, tracking, Time.unscaledDeltaTime);
                if (action == ArRecoveryAction.ResetSession) ResetSessionKeepingMob();
                frame = new ArNoWalkFrame(ArNoWalkAction.None, YawPose(mobT), false, _recovery.MobUsable, _recovery.MobUsable, ArMobMotion.Paused, 0.0);
            }
            else
            {
                bool alive = Combat == null || Combat.Session == null || !Combat.Session.MobDefeated;
                bool settlePending = _noWalk.PlaneSettlePending;
                double? planeY = settlePending && tracking && TryPlaneHeightUnder(mobT.position, _noWalk.RestoredY, out float py) ? py : (double?)null;
                frame = _noWalk.Tick(tracking, YawPose(_arCamera.transform), YawPose(mobT), Time.unscaledDeltaTime, MobApproachEnabled && alive, planeY);
                if (settlePending && !_noWalk.PlaneSettlePending)
                {
                    Debug.Log("[ArSession] mob settled on plane y=" + frame.MobPose.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " restoredY=" + _noWalk.RestoredY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                              + " dy=" + (frame.MobPose.Y - _noWalk.RestoredY).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }

                if (frame.Action == ArNoWalkAction.ResetSession)
                {
                    ResetSessionKeepingMob();
                }
                else if (frame.Action == ArNoWalkAction.Restore)
                {
                    RestoreMobCameraRelative(frame);
                }
                else if (frame.MobMoved)
                {
                    // Y changes only while easing onto a plane found after a restore (#97 Q2), within the step limit.
                    mobT.SetPositionAndRotation(new Vector3((float)frame.MobPose.X, (float)frame.MobPose.Y, (float)frame.MobPose.Z),
                                                Quaternion.Euler(0f, (float)frame.MobPose.YawDegrees, 0f));
                }
            }

            CombatAllowed = frame.CombatAllowed;
            LastMobStepMeters = (float)frame.StepMeters;
            MaxMobStepMeters = Mathf.Max(MaxMobStepMeters, LastMobStepMeters);
            MobMotion = frame.Motion;
            double cruise = _noWalkSettings.approachSpeedMetersPerSecond;
            SpawnedMob.SetGait(_noWalk.Approach.TravelledMeters, frame.Motion == ArMobMotion.Approaching || frame.Motion == ArMobMotion.Retreating
                ? (float)(_noWalk.Approach.Speed / cruise) : 0f);
            if (MobMotion != _loggedMotion)
            {
                Vector3 c = _arCamera != null ? _arCamera.transform.position : mobT.position;
                Vector3 m = mobT.position;
                Debug.Log("[ArEncounter] mob motion " + _loggedMotion + "->" + MobMotion
                          + " d=" + ArEncounterFraming.HorizontalDistance(c.x, c.z, m.x, m.z).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                          + " d_stop=" + MobStopDistance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                          + " maxStep=" + MaxMobStepMeters.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                          + " " + FightState());
                if (MobMotion == ArMobMotion.Holding && (_loggedMotion == ArMobMotion.Approaching || _loggedMotion == ArMobMotion.Retreating))
                {
                    // The mob moved on purpose: the anchor probe measures drift from its new resting pose.
                    ResetAnchorProbe();
                }

                _loggedMotion = MobMotion;
            }

            bool visible = _recovery.MobUsable;
            if (visible != SpawnedMob.IsTrackingVisible)
            {
                SpawnedMob.SetTrackingVisible(visible);
                if (!visible)
                {
                    FppCombat?.CancelActions();
                }

                Debug.Log("[ArEncounter] mob " + (visible
                    ? "shown (tracking)"
                    : "hidden (" + (IsRelocatingMob ? "waiting for tracking after reset" : "tracking lost") + ": " + LastSessionStatus + ")"));
            }
        }

        /// <summary>
        /// Height of a tracked horizontal (up) plane whose extents contain <paramref name="position"/> (x/z), the one closest
        /// to <paramref name="referenceY"/>. Used after a restore to ease the mob onto real ground (#97 Q2).
        /// </summary>
        private bool TryPlaneHeightUnder(Vector3 position, double referenceY, out float planeY)
        {
            planeY = 0f;
            if (_planeManager == null)
            {
                return false;
            }

            bool found = false;
            double best = double.MaxValue;
            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp || plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null)
                {
                    continue;
                }

                Vector3 local = plane.transform.InverseTransformPoint(position);
                Vector2 c = plane.centerInPlaneSpace;
                Vector2 e = plane.extents;
                if (Mathf.Abs(local.x - c.x) > e.x || Mathf.Abs(local.z - c.y) > e.y)
                {
                    continue;
                }

                float y = plane.transform.position.y;
                double gap = System.Math.Abs(y - referenceY);
                if (gap < best)
                {
                    best = gap;
                    planeY = y;
                    found = true;
                }
            }

            return found;
        }

        private static ArYawPose YawPose(Transform t)
        {
            Vector3 p = t.position;
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            double yaw = f.sqrMagnitude > 1e-8f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : t.eulerAngles.y;
            return new ArYawPose(p.x, p.y, p.z, yaw);
        }

        private string FightState()
        {
            var fight = Combat != null ? Combat.Session : null;
            return fight == null ? "hp=- defeated=-" : "hp=" + fight.MobHp + "/" + fight.MobMaxHp + " defeated=" + fight.MobDefeated;
        }

        private void ResetSessionKeepingMob()
        {
            Debug.Log("[ArSession] reset reason=tracking-lost lostFor=" + _recovery.LastResetLostSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                      + "s limit=" + _recovery.ResetAfterSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                      + "s count=" + _recovery.ResetCount + " session=" + LastSessionStatus + " anchor=" + MobAnchorKind
                      + " " + FightState() + " t=" + Time.realtimeSinceStartup.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            FppCombat?.CancelActions();
            Transform mob = SpawnedMob.transform;
            // Keep the mob object (and its fight) alive: anchors are removed by the reset and would take their children with them.
            mob.SetParent(_origin != null ? _origin.TrackablesParent : null, true);
            _anchorGeneration++;
            if (MobAnchor != null && _anchorManager != null)
            {
                try
                {
                    _anchorManager.TryRemoveAnchor(MobAnchor);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ArSession] old anchor removal failed: " + ex.Message);
                }
            }

            MobAnchor = null;
            MobAnchorKind = "relocating";
            if (_session != null && _session.subsystem != null)
            {
                _session.Reset();
            }
        }

        /// <summary>
        /// #97 rule 7: after a session reset, tracking is back. The mob is put at the same horizontal distance and bearing
        /// relative to the camera (and the same height below it) as in the last tracked frame, distance clamped to
        /// [d_min, 3.0 m], then anchored with a free world anchor. No reticle, no new plane: the player does not re-place
        /// or walk. HP and the reward state are untouched (the fight object is never recreated).
        /// </summary>
        private void RestoreMobCameraRelative(ArNoWalkFrame frame)
        {
            Transform mob = SpawnedMob.transform;
            var pose = new Pose(new Vector3((float)frame.MobPose.X, (float)frame.MobPose.Y, (float)frame.MobPose.Z),
                                Quaternion.Euler(0f, (float)frame.MobPose.YawDegrees, 0f));
            mob.SetPositionAndRotation(pose.position, pose.rotation);
            MobAnchorKind = "session";
            if (_anchorManager != null && _anchorManager.enabled && _anchorManager.subsystem != null)
            {
                MobAnchorKind = "pending";
                AnchorMobAtWorldPose(mob, pose, null);
            }

            _noWalk.MarkRestored();
            ResetAnchorProbe();
            Vector3 c = _arCamera != null ? _arCamera.transform.position : pose.position;
            ArCameraRelativePose rel = _noWalk.LastRelativePose;
            Debug.Log("[ArSession] restored mob camera-relative at " + pose.position.ToString("F2")
                      + " d=" + ArEncounterFraming.HorizontalDistance(c.x, c.z, pose.position.x, pose.position.z).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " recordedD=" + (rel.IsValid ? rel.DistanceMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "n/a")
                      + " bearing=" + (rel.IsValid ? rel.BearingDegrees.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "n/a")
                      + " planeBelowCam=" + ArEncounterFraming.PlaneDropBelowCamera(c.y, pose.position.y).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " anchor=" + MobAnchorKind + " resets=" + _recovery.ResetCount + " " + FightState());
        }

        /// <summary>
        /// Distance-only attack reach (#31) as a diagnostic: logs <c>[ArEncounter] reach in|out …</c> on every change.
        /// #97 removed the „Podejdź – x,x m” prompt — the mob walks into reach by itself.
        /// </summary>
        private void UpdateReachDiagnostics()
        {
            if (SpawnedMob == null || _arCamera == null)
            {
                return;
            }

            Vector3 c = _arCamera.transform.position;
            Vector3 m = SpawnedMob.transform.position;
            ApproachDistance = (float)ArEncounterFraming.HorizontalDistance(c.x, c.z, m.x, m.z);
            FootprintDistance = FppCombat != null ? FppCombat.HorizontalDistanceToHitFootprint(SpawnedMob) : float.NaN;
            InAttackReach = FppCombat != null && !float.IsNaN(FootprintDistance)
                            && !ArApproachPrompt.IsOutOfReach(FootprintDistance, FppCombatController.BladeReachMeters);
            if (InAttackReach == _loggedInReach)
            {
                return;
            }

            _loggedInReach = InAttackReach;
            Debug.Log("[ArEncounter] reach " + (InAttackReach ? "in" : "out")
                      + " d=" + ApproachDistance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " toFootprint=" + (float.IsNaN(FootprintDistance) ? "n/a" : FootprintDistance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                      + " bladeReach=" + FppCombatController.BladeReachMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " motion=" + MobMotion);
        }

        private bool IsFrameTargetSuitable()
        {
            foreach (ArPlaneObservation plane in _frame.Planes)
            {
                if (plane.Id == _frame.TargetPlaneId)
                {
                    return _machine.IsSuitable(plane);
                }
            }

            return false;
        }

        /// <summary>#31 P1: placement surface not more than the prototype limit below the AR camera (true without a camera).</summary>
        public bool IsPlaneHeightAcceptable(Vector3 target)
        {
            if (_arCamera == null)
            {
                return true;
            }

            return ArEncounterFraming.IsPlaneHeightAcceptable(_arCamera.transform.position.y, target.y, _machine.Settings.maxPlaneDropBelowCameraMeters);
        }

        /// <summary>Logs the target plane height relative to the camera once per plane / verdict change (#31 P1).</summary>
        private void LogTargetPlaneHeight(string planeId, Vector3 target, bool accepted)
        {
            if (planeId == _loggedTargetPlane && accepted == _loggedTargetAccepted)
            {
                return;
            }

            _loggedTargetPlane = planeId;
            _loggedTargetAccepted = accepted;
            Vector3 c = _arCamera != null ? _arCamera.transform.position : target;
            double drop = ArEncounterFraming.PlaneDropBelowCamera(c.y, target.y);
            Debug.Log("[ArEncounter] target plane=" + planeId
                      + " planeBelowCam=" + drop.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " maxBelow=" + _machine.Settings.maxPlaneDropBelowCameraMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " range=" + _machine.Settings.minSpawnDistanceMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                      + "-" + _machine.Settings.maxSpawnDistanceMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                      + " d=" + ArEncounterFraming.HorizontalDistance(c.x, c.z, target.x, target.z).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + (accepted ? " accepted" : " rejected"));
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
                PlacementTargetTooClose,
                !MobUsable,
                PlacementTargetTooFar,
                MobMotion);
        }

        /// <summary>Placement target at least the minimum ground distance from the AR camera (true without a camera).</summary>
        public bool IsFarEnough(Vector3 target)
        {
            if (_arCamera == null)
            {
                return true;
            }

            Vector3 c = _arCamera.transform.position;
            return ArEncounterFraming.ClassifySpawnDistance(c.x, c.z, target.x, target.z,
                       _machine.Settings.minSpawnDistanceMeters, double.PositiveInfinity) == ArSpawnDistance.InRange;
        }

        /// <summary>Placement target not beyond the v0 maximum spawn distance (#31 decision 18:58, PROTOTYPE 3.0 m; true without a camera).</summary>
        public bool IsNearEnough(Vector3 target)
        {
            if (_arCamera == null)
            {
                return true;
            }

            Vector3 c = _arCamera.transform.position;
            return ArEncounterFraming.ClassifySpawnDistance(c.x, c.z, target.x, target.z,
                       0.0, _machine.Settings.maxSpawnDistanceMeters) == ArSpawnDistance.InRange;
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

                        if (!IsNearEnough(hit.pose.position))
                        {
                            frame.TargetTooFar = true;
                            continue;
                        }

                        if (!IsPlaneHeightAcceptable(hit.pose.position))
                        {
                            frame.TargetTooLow = true;
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

            if (!IsNearEnough(nearestSuitable.center))
            {
                frame.TargetTooFar = true;
                return;
            }

            if (!IsPlaneHeightAcceptable(nearestSuitable.center))
            {
                frame.TargetTooLow = true;
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
            go.transform.localScale = _mobPrefab.transform.localScale * MobVisualScale;
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
            Vector3 camAtSpawn = _arCamera != null ? _arCamera.transform.position : pose.position;
            Debug.Log("[ArEncounter] spawned " + Request.MobName + " at " + pose.position + " anchor=" + MobAnchorKind
                      + " scale=" + MobVisualScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " planeBelowCam=" + ArEncounterFraming.PlaneDropBelowCamera(camAtSpawn.y, pose.position.y).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " d=" + ArEncounterFraming.HorizontalDistance(camAtSpawn.x, camAtSpawn.z, pose.position.x, pose.position.z).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                      + " range=" + _machine.Settings.minSpawnDistanceMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                      + "-" + _machine.Settings.maxSpawnDistanceMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            MobPlaced?.Invoke(SpawnedMob);
        }

        private async void AnchorMobAtWorldPose(Transform mob, Pose pose, ARPlane plane)
        {
            int generation = _anchorGeneration;
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

            if (this == null || mob == null || IsExiting || generation != _anchorGeneration)
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

