using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// The AR view's status line, engine-free so it can be checked exhaustively. #97 rule 2: the AR view never asks the
    /// player to walk, approach or back off ("Podejdź", "Podejdź bliżej", "Odsuń się" are gone); placement hints only ask
    /// to aim the phone (tilt it up or down). The GPS map keeps its own walking prompts (rule 1) — they are not in this class.
    /// </summary>
    public static class ArEncounterStatusText
    {
        public const string Starting = "Uruchamianie AR...";

        /// <summary>Lead decision 2026-10-23 04:42 (#97 Q7): the scan hint says to move the PHONE while standing still.</summary>
        public const string Scanning = "Skanuj teren: poruszaj telefonem, stojąc w miejscu, i celuj w płaskie podłoże.";

        public const string ScanningNoTrackingSuffix = "\nBrak śledzenia — sprawdź zgodę na aparat i oświetlenie.";

        public const string ReadyWithTarget = "Podłoże wykryte. Naciśnij \"Postaw potwora\".";

        public const string ReadyNoTarget = "Podłoże wykryte. Wyceluj w podłoże.";

        /// <summary>Placed, tracking lost or waiting for new ground after an AR session reset (#31 MANAGER DECISION 18:50).</summary>
        public const string SearchingSurroundings = "Szukam otoczenia…";

        /// <summary>Ground under the reticle is closer than the minimum spawn distance: aim farther (tilt the phone up).</summary>
        public static readonly string PlacementTooClose =
            "Za blisko. Unieś telefon i wyceluj dalej w podłoże (min. " + Meters(ArEncounterFraming.MinPlacementDistanceMeters) + ").";

        /// <summary>Ground under the reticle is beyond the v0 maximum spawn distance: aim nearer (tilt the phone down).</summary>
        public static readonly string PlacementTooFar =
            "Za daleko. Pochyl telefon i wyceluj bliżej w podłoże (maks. " + Meters(ArEncounterFraming.MaxPlacementDistanceMeters) + ").";

        public static string For(ArEncounterState state, bool hasPlacementTarget, float notTrackingSeconds, string mobName,
                                 bool targetTooClose, bool targetTooFar, bool mobHidden, bool trackingLimited, ArMobMotion motion)
        {
            switch (state)
            {
                case ArEncounterState.Starting:
                    return Starting;
                case ArEncounterState.Scanning:
                    return notTrackingSeconds > 10f ? Scanning + ScanningNoTrackingSuffix : Scanning;
                case ArEncounterState.ReadyToPlace:
                    return hasPlacementTarget ? ReadyWithTarget
                        : targetTooClose ? PlacementTooClose
                        : targetTooFar ? PlacementTooFar : ReadyNoTarget;
                case ArEncounterState.Placed:
                    if (mobHidden || trackingLimited)
                    {
                        return SearchingSurroundings;
                    }

                    // Lead decision 04:42 (#97 Q3): approved; mobName is the display name in the nominative (e.g. "Trujący Pająk"),
                    // and both verbs are 3rd person singular present, which do not inflect for gender.
                    return motion == ArMobMotion.Approaching
                        ? mobName + " nadchodzi."
                        : mobName + " stoi na podłożu.";
                default:
                    return string.Empty;
            }
        }

        private static string Meters(double m)
        {
            return m.ToString("0.0#", CultureInfo.InvariantCulture).Replace('.', ',') + " m";
        }
    }
}
