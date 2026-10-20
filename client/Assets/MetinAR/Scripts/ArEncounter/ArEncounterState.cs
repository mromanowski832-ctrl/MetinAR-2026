namespace MetinAR.ArEncounter
{
    /// <summary>Observable state of the AR encounter prototype.</summary>
    public enum ArEncounterState
    {
        /// <summary>AR availability is being checked / the session is not running yet.</summary>
        Starting = 0,

        /// <summary>The session runs; no suitable horizontal ground plane yet. Scanning instruction is shown.</summary>
        Scanning = 1,

        /// <summary>A tracked horizontal ground plane is suitable; the monster can be placed.</summary>
        ReadyToPlace = 2,

        /// <summary>The single test monster was placed on the ground (terminal for placement).</summary>
        Placed = 3,

        /// <summary>AR is not supported on this device (terminal).</summary>
        Unsupported = 4,

        /// <summary>The AR session failed or did not start in time (recoverable if the session starts later).</summary>
        Failed = 5,
    }

    /// <summary>
    /// Project-owned mirror of AR Foundation's <c>ARSessionState</c> so the state logic stays pure C#.
    /// The Unity adapter maps one to the other 1:1.
    /// </summary>
    public enum ArSessionStatus
    {
        None = 0,
        Unsupported = 1,
        CheckingAvailability = 2,
        NeedsInstall = 3,
        Installing = 4,
        Ready = 5,
        SessionInitializing = 6,
        SessionTracking = 7,
    }

    /// <summary>Result of a placement attempt.</summary>
    public enum ArPlacementResult
    {
        /// <summary>The monster must be spawned now (returned exactly once per encounter).</summary>
        Placed = 0,

        /// <summary>The monster was already placed; nothing must be spawned.</summary>
        AlreadyPlaced = 1,

        /// <summary>Not ready (scanning / starting / unsupported / failed); nothing must be spawned.</summary>
        NotReady = 2,

        /// <summary>The given plane is unknown or not suitable; nothing must be spawned.</summary>
        UnsuitablePlane = 3,
    }
}
