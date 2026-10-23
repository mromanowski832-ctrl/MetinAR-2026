using System;
using System.Globalization;

namespace MetinAR.Map
{
    /// <summary>
    /// METINAR-042 (#87): engine-free text for map encounter markers and the "nearby monster" notice,
    /// e.g. "Trujący Pająk — 50 m" (owner spec 2026-10-23 §2A point 6) and "Poz. 5".
    /// Distances are rounded so GPS noise does not make the number flicker; no coordinates are ever formatted.
    /// </summary>
    public static class EncounterLabelFormatter
    {
        /// <summary>Em dash with spaces, as written in the owner spec.</summary>
        public const string Separator = " \u2014 ";

        /// <summary>
        /// Rounds a distance for display: below 10 m → whole metres (min 1), below 100 m → 5 m steps,
        /// below 1000 m → 10 m steps; 1 km and more → kilometres with one decimal ("1,2 km").
        /// </summary>
        public static string FormatDistance(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0)
            {
                return "? m";
            }

            if (meters < 10)
            {
                return Math.Max(1, (int)Math.Round(meters, MidpointRounding.AwayFromZero)) + " m";
            }

            if (meters < 100)
            {
                return (int)(Math.Round(meters / 5.0, MidpointRounding.AwayFromZero) * 5) + " m";
            }

            if (meters < 995)
            {
                return (int)(Math.Round(meters / 10.0, MidpointRounding.AwayFromZero) * 10) + " m";
            }

            double km = Math.Round(meters / 1000.0, 1, MidpointRounding.AwayFromZero);
            // Polish decimal comma without depending on culture data (IL2CPP may strip it).
            return km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";
        }

        /// <summary>"Poz. 5" — same wording as <c>ArEncounterRequest.LevelLabel</c>.</summary>
        public static string FormatLevel(int level)
        {
            return "Poz. " + Math.Max(1, level);
        }

        /// <summary>"Trujący Pająk — 50 m" (the nearby-monster notice).</summary>
        public static string FormatNotice(string name, double distanceMeters)
        {
            return SafeName(name) + Separator + FormatDistance(distanceMeters);
        }

        /// <summary>Two-line marker label: "Trujący Pająk (Poz. 5)\n50 m"; aggressive mobs get a "!" marker.</summary>
        public static string FormatMarker(string name, int level, double distanceMeters, bool aggressive)
        {
            return SafeName(name) + " (" + FormatLevel(level) + ")" + (aggressive ? " !" : string.Empty)
                   + "\n" + FormatDistance(distanceMeters);
        }

        /// <summary>Short status line under the marker / on the "Walcz" button area.</summary>
        public static string FormatAvailability(EncounterAvailability availability, double distanceToEnterMeters)
        {
            switch (availability)
            {
                case EncounterAvailability.Available:
                    return "Spotkanie AR dostępne \u2014 Walcz";
                case EncounterAvailability.TooFar:
                    return "Podejdź bliżej" + (distanceToEnterMeters > 0 ? " (" + FormatDistance(distanceToEnterMeters) + ")" : string.Empty);
                case EncounterAvailability.PoorAccuracy:
                    return "Słaby sygnał GPS \u2014 poczekaj";
                case EncounterAvailability.StaleFix:
                    return "Brak aktualnej pozycji GPS";
                case EncounterAvailability.UnsafeSite:
                    return "Niedostępne w tym miejscu";
                default:
                    return string.Empty;
            }
        }

        private static string SafeName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();
        }
    }
}
