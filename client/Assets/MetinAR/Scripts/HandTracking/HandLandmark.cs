namespace MetinAR.HandTracking
{
    /// <summary>
    /// Indices of the 21 hand landmarks produced by the MediaPipe hand-landmark model (wrist, then four joints per
    /// finger from thumb to pinky). Pure C# (no UnityEngine) so it is covered by the .NET tests.
    /// </summary>
    public static class HandLandmark
    {
        public const int Count = 21;

        public const int Wrist = 0;
        public const int ThumbCmc = 1;
        public const int ThumbMcp = 2;
        public const int ThumbIp = 3;
        public const int ThumbTip = 4;
        public const int IndexMcp = 5;
        public const int IndexPip = 6;
        public const int IndexDip = 7;
        public const int IndexTip = 8;
        public const int MiddleMcp = 9;
        public const int MiddlePip = 10;
        public const int MiddleDip = 11;
        public const int MiddleTip = 12;
        public const int RingMcp = 13;
        public const int RingPip = 14;
        public const int RingDip = 15;
        public const int RingTip = 16;
        public const int PinkyMcp = 17;
        public const int PinkyPip = 18;
        public const int PinkyDip = 19;
        public const int PinkyTip = 20;

        /// <summary>Landmarks that MediaPipe uses to derive the tracking region from the previous frame (palm + lower joints).</summary>
        public static readonly int[] RoiSubset = { 0, 1, 2, 3, 5, 6, 9, 10, 13, 14, 17, 18 };
    }
}
