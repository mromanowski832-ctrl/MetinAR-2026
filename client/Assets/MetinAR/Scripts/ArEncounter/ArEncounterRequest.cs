using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Minimal data that identifies the test mob for an AR encounter (prototype; no backend).
    /// Built by the world map when an encounter is requested and read by the AR encounter scene.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class ArEncounterRequest
    {
        public ArEncounterRequest(string mobName, int level, int currentHp, int maxHp, bool aggressive, string reason)
        {
            if (string.IsNullOrWhiteSpace(mobName))
            {
                throw new ArgumentException("Mob name is required.", nameof(mobName));
            }

            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be at least 1.");
            }

            if (maxHp < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "Max HP must be at least 1.");
            }

            if (currentHp < 0 || currentHp > maxHp)
            {
                throw new ArgumentOutOfRangeException(nameof(currentHp), currentHp, "Current HP must be within 0..maxHp.");
            }

            MobName = mobName;
            Level = level;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            Aggressive = aggressive;
            Reason = reason ?? string.Empty;
        }

        /// <summary>Display name of the mob, e.g. "Trujący Pająk".</summary>
        public string MobName { get; }

        public int Level { get; }

        public int CurrentHp { get; }

        public int MaxHp { get; }

        public bool Aggressive { get; }

        /// <summary>Why the encounter was requested, e.g. "panel:Trujący Pająk" or "aggro:Trujący Pająk".</summary>
        public string Reason { get; }

        /// <summary>Label shown above the AR enemy: level line.</summary>
        public string LevelLabel => "Poz. " + Level;

        /// <summary>Label shown in the AR enemy HP bar.</summary>
        public string HpLabel => "HP " + CurrentHp + "/" + MaxHp;

        /// <summary>HP bar fill 0..1.</summary>
        public float HpFraction => (float)CurrentHp / MaxHp;

        /// <summary>
        /// The prototype test mob (owner-confirmed prototype stats: level 5, HP 120/120, aggressive).
        /// Used only when the AR prototype scene is opened directly without a world-map request.
        /// </summary>
        public static ArEncounterRequest PrototypeTestSpider(string reason)
        {
            return new ArEncounterRequest("Trujący Pająk", 5, 120, 120, true, reason);
        }

        public override string ToString()
        {
            return MobName + " (" + LevelLabel + ", " + HpLabel + ", reason=" + Reason + ")";
        }
    }
}
