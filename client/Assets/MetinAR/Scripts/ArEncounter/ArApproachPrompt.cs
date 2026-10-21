using System;
using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// #31 decision 18:58: in v0 the mob stands still and the player walks up to it. While the placed, visible, undefeated
    /// mob is outside attack reach the HUD shows „Podejdź – 2,4 m” (horizontal camera-to-mob distance, one decimal).
    /// Hidden in reach, after the kill and while tracking is lost („Szukam otoczenia…” has priority). Engine-free.
    /// </summary>
    public static class ArApproachPrompt
    {
        public const string Prefix = "Podejdź – ";

        /// <summary>„Podejdź – 2,4 m” (Polish decimal comma, rounded half away from zero); empty for a non-finite or negative distance.</summary>
        public static string Format(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0) return string.Empty;
            double rounded = Math.Round(meters, 1, MidpointRounding.AwayFromZero);
            return Prefix + rounded.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " m";
        }

        /// <summary>True only for a placed, usable (tracking, anchored) and undefeated mob that is out of attack reach.</summary>
        public static bool ShouldShow(bool placed, bool mobUsable, bool mobDefeated, bool inAttackReach, double meters)
        {
            return placed && mobUsable && !mobDefeated && !inAttackReach
                   && !double.IsNaN(meters) && !double.IsInfinity(meters) && meters >= 0;
        }
    }
}
