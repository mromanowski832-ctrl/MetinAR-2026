namespace MetinAR.ArEncounter
{
    /// <summary>
    /// What the state logic needs to know about one detected AR plane (pure C# snapshot of an ARPlane).
    /// </summary>
    public readonly struct ArPlaneObservation
    {
        public ArPlaneObservation(string id, bool isHorizontalUp, bool isTracking, float widthMeters, float lengthMeters, bool isSubsumed)
        {
            Id = id;
            IsHorizontalUp = isHorizontalUp;
            IsTracking = isTracking;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            IsSubsumed = isSubsumed;
        }

        /// <summary>Trackable id as string.</summary>
        public string Id { get; }

        /// <summary>Plane alignment is horizontal and facing up (ground / floor / table).</summary>
        public bool IsHorizontalUp { get; }

        /// <summary>Plane tracking state is <c>Tracking</c> (not limited / none).</summary>
        public bool IsTracking { get; }

        public float WidthMeters { get; }

        public float LengthMeters { get; }

        /// <summary>Plane was merged into another plane.</summary>
        public bool IsSubsumed { get; }

        public override string ToString()
        {
            return Id + " up=" + IsHorizontalUp + " tracking=" + IsTracking + " size=" + WidthMeters + "x" + LengthMeters
                   + (IsSubsumed ? " subsumed" : string.Empty);
        }
    }
}
