using System;
using System.Collections.Generic;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Technical prototype thresholds for <see cref="ArEncounterStateMachine"/> (not game design).
    /// </summary>
    [Serializable]
    public sealed class ArPlacementSettings
    {
        /// <summary>Minimum plane width and length (meters) to count as suitable ground. Prototype: 0.3 m.</summary>
        public float minPlaneExtentMeters = 0.3f;

        /// <summary>
        /// Seconds the encounter may stay in <see cref="ArEncounterState.Starting"/> before it reports
        /// <see cref="ArEncounterState.Failed"/> (e.g. camera permission denied, ARCore install declined).
        /// Prototype: 20 s.
        /// </summary>
        public float startTimeoutSeconds = 20f;

        /// <summary>
        /// Seconds a placed encounter may stay without session tracking before the ARSession is reset
        /// (#31 MANAGER DECISION 18:50). PROTOTYPE: 15 s.
        /// </summary>
        public float trackingLostResetSeconds = 15f;

        /// <summary>
        /// A placement surface more than this far below the AR camera is rejected (#31 P1: A31 picked a plane 2.91 m below
        /// a handheld phone). PROTOTYPE: 2.5 m.
        /// </summary>
        public float maxPlaneDropBelowCameraMeters = 2.5f;

        public static ArPlacementSettings CreatePrototypeDefaults()
        {
            return new ArPlacementSettings();
        }
    }

    /// <summary>
    /// Pure state logic of the AR encounter prototype:
    /// Starting → Scanning → ReadyToPlace → Placed, plus Unsupported / Failed.
    /// The Unity adapter feeds it AR Foundation session state and plane snapshots; it decides
    /// when the single test monster may be spawned (at most once per encounter).
    /// No UnityEngine dependency.
    /// </summary>
    public sealed class ArEncounterStateMachine
    {
        private readonly ArPlacementSettings _settings;
        private readonly HashSet<string> _suitablePlaneIds = new HashSet<string>();
        private float _startingSeconds;

        public ArEncounterStateMachine(ArPlacementSettings settings = null)
        {
            _settings = settings ?? ArPlacementSettings.CreatePrototypeDefaults();
            if (!(_settings.minPlaneExtentMeters >= 0f) || float.IsInfinity(_settings.minPlaneExtentMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "minPlaneExtentMeters must be finite and >= 0.");
            }

            if (!(_settings.startTimeoutSeconds > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "startTimeoutSeconds must be > 0.");
            }

            State = ArEncounterState.Starting;
            SessionStatus = ArSessionStatus.None;
        }

        /// <summary>Raised on every state change (previous, next).</summary>
        public event Action<ArEncounterState, ArEncounterState> StateChanged;

        public ArPlacementSettings Settings => _settings;

        public ArEncounterState State { get; private set; }

        public ArSessionStatus SessionStatus { get; private set; }

        /// <summary>Number of times <see cref="TryPlace"/> returned <see cref="ArPlacementResult.Placed"/> (0 or 1).</summary>
        public int PlacementCount { get; private set; }

        /// <summary>Plane the monster was placed on (null before placement).</summary>
        public string PlacedPlaneId { get; private set; }

        /// <summary>After placement: the session currently does not track (monster stays anchored).</summary>
        public bool IsTrackingLimited { get; private set; }

        /// <summary>Human-readable failure reason when <see cref="State"/> is Failed or Unsupported.</summary>
        public string FailureReason { get; private set; }

        /// <summary>Suitable plane ids from the latest plane update.</summary>
        public IReadOnlyCollection<string> SuitablePlaneIds => _suitablePlaneIds;

        public bool IsTerminalFailure => State == ArEncounterState.Unsupported;

        /// <summary>True when a horizontal plane with the given properties counts as ground for placement.</summary>
        public bool IsSuitable(ArPlaneObservation plane)
        {
            return !string.IsNullOrEmpty(plane.Id)
                   && plane.IsHorizontalUp
                   && plane.IsTracking
                   && !plane.IsSubsumed
                   && plane.WidthMeters >= _settings.minPlaneExtentMeters
                   && plane.LengthMeters >= _settings.minPlaneExtentMeters;
        }

        /// <summary>Feeds the current AR session status (mirror of ARSessionState).</summary>
        public void SetSessionStatus(ArSessionStatus status)
        {
            SessionStatus = status;
            if (State == ArEncounterState.Unsupported)
            {
                return; // terminal
            }

            if (State == ArEncounterState.Placed)
            {
                // The monster stays where it was anchored; only report degraded tracking.
                IsTrackingLimited = status != ArSessionStatus.SessionTracking;
                return;
            }

            if (status == ArSessionStatus.Unsupported)
            {
                FailureReason = "AR nie jest obsługiwane na tym urządzeniu.";
                _suitablePlaneIds.Clear();
                SetState(ArEncounterState.Unsupported);
                return;
            }

            bool running = status == ArSessionStatus.SessionInitializing || status == ArSessionStatus.SessionTracking;

            if (running)
            {
                FailureReason = null;
                _startingSeconds = 0f;
                if (status != ArSessionStatus.SessionTracking)
                {
                    _suitablePlaneIds.Clear();
                }

                SetState(status == ArSessionStatus.SessionTracking && _suitablePlaneIds.Count > 0
                    ? ArEncounterState.ReadyToPlace
                    : ArEncounterState.Scanning);
                return;
            }

            // None / CheckingAvailability / NeedsInstall / Installing / Ready: session not running.
            _suitablePlaneIds.Clear();
            if (State != ArEncounterState.Failed)
            {
                SetState(ArEncounterState.Starting);
            }
        }

        /// <summary>Advances the start timeout. Call once per frame with the frame time.</summary>
        public void Tick(float deltaSeconds)
        {
            if (State != ArEncounterState.Starting || !(deltaSeconds > 0f) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            _startingSeconds += deltaSeconds;
            if (_startingSeconds >= _settings.startTimeoutSeconds)
            {
                Fail("Sesja AR nie wystartowała (brak zgody na kamerę, brak usług AR lub błąd sesji).");
            }
        }

        /// <summary>Reports an explicit session failure (e.g. no XR provider loaded). Recoverable if the session starts later.</summary>
        public void Fail(string reason)
        {
            if (State == ArEncounterState.Unsupported || State == ArEncounterState.Placed)
            {
                return;
            }

            FailureReason = string.IsNullOrEmpty(reason) ? "Błąd sesji AR." : reason;
            _suitablePlaneIds.Clear();
            SetState(ArEncounterState.Failed);
        }

        /// <summary>
        /// Feeds the current set of detected planes. Ready-to-place requires a tracking session and at
        /// least one suitable tracked horizontal plane.
        /// </summary>
        public void UpdatePlanes(IEnumerable<ArPlaneObservation> planes)
        {
            if (State != ArEncounterState.Scanning && State != ArEncounterState.ReadyToPlace)
            {
                return;
            }

            _suitablePlaneIds.Clear();
            if (planes != null && SessionStatus == ArSessionStatus.SessionTracking)
            {
                foreach (ArPlaneObservation plane in planes)
                {
                    if (IsSuitable(plane))
                    {
                        _suitablePlaneIds.Add(plane.Id);
                    }
                }
            }

            SetState(_suitablePlaneIds.Count > 0 ? ArEncounterState.ReadyToPlace : ArEncounterState.Scanning);
        }

        /// <summary>
        /// Requests placement on <paramref name="planeId"/>. Returns <see cref="ArPlacementResult.Placed"/>
        /// exactly once per encounter; the caller spawns the monster only for that result.
        /// </summary>
        public ArPlacementResult TryPlace(string planeId)
        {
            if (State == ArEncounterState.Placed)
            {
                return ArPlacementResult.AlreadyPlaced;
            }

            if (State != ArEncounterState.ReadyToPlace)
            {
                return ArPlacementResult.NotReady;
            }

            if (string.IsNullOrEmpty(planeId) || !_suitablePlaneIds.Contains(planeId))
            {
                return ArPlacementResult.UnsuitablePlane;
            }

            PlacementCount++;
            PlacedPlaneId = planeId;
            IsTrackingLimited = false;
            SetState(ArEncounterState.Placed);
            return ArPlacementResult.Placed;
        }

        private void SetState(ArEncounterState next)
        {
            if (next == State)
            {
                return;
            }

            ArEncounterState previous = State;
            State = next;
            if (next == ArEncounterState.Starting)
            {
                _startingSeconds = 0f;
            }

            StateChanged?.Invoke(previous, next);
        }
    }
}
