using System;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Summed equipment bonuses that the spec adds as "+ przedmioty" (CHARACTER_PROGRESSION.md §1.3).
    /// Fractions are 0…1 (5% = 0.05). Caps are applied by <see cref="DerivedStatsCalculator"/>.
    /// </summary>
    public sealed class EquipmentBonuses
    {
        public double MaxHp;
        public double MaxMp;
        /// <summary>Σ item defense.</summary>
        public double Defense;
        public double Accuracy;
        public double Evasion;
        public double CritChance;
        /// <summary>Added to the 1.6 crit multiplier.</summary>
        public double CritMultiplier;
        /// <summary>Shield block + affixes; only counts when <see cref="HasShield"/>.</summary>
        public double BlockChance;
        public bool HasShield;
        /// <summary>HP regenerated per 5 s in combat (affix "Regeneracja HP/5 s").</summary>
        public double HpRegenPer5s;
        public double ResistPoison;
        public double ResistDark;
        public double ResistElemental;

        public static EquipmentBonuses None { get { return new EquipmentBonuses(); } }
    }

    /// <summary>Equipped weapon as seen by the stat formulas.</summary>
    public sealed class WeaponLoadout
    {
        /// <summary>Weapon base attack after rarity (e.g. starter sword Atk 6, or WeaponAtk(iL) × rarity).</summary>
        public double BaseAtk;
        /// <summary>Weapon base magic power after rarity.</summary>
        public double BaseMag;
        /// <summary>Temper +0…+10.</summary>
        public int TemperLevel;
        /// <summary>Stat that scales Atk: Might for swords/axes, Finesse for daggers/bow.</summary>
        public PrimaryStat ScalingStat = PrimaryStat.Might;

        public static WeaponLoadout Unarmed { get { return new WeaponLoadout(); } }

        /// <summary>
        /// Scaling stat for a weapon item type from tables/item_slots.csv. Swords and axes → Might, daggers and
        /// bow → Finesse. Returns false for types the spec does not assign (e.g. rune_wand).
        /// </summary>
        public static bool TryGetScalingStat(string itemType, out PrimaryStat stat)
        {
            switch (itemType)
            {
                case "sword_1h":
                case "sword_2h":
                case "axe_2h":
                    stat = PrimaryStat.Might; return true;
                case "dagger_pair":
                case "bow":
                    stat = PrimaryStat.Finesse; return true;
                default:
                    stat = PrimaryStat.Might; return false;
            }
        }

        /// <summary>A plain generated weapon: Atk = (4 + 1.6 × iL) × rarity multiplier.</summary>
        public static WeaponLoadout PlainWeapon(BalanceConfig config, int itemLevel, string rarityId, int temperLevel, PrimaryStat scalingStat)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new WeaponLoadout
            {
                BaseAtk = config.WeaponAtk(itemLevel) * config.GetRarity(rarityId).BaseStatMultiplier,
                TemperLevel = temperLevel,
                ScalingStat = scalingStat,
            };
        }
    }

    /// <summary>Class, level and the free points the player allocated (2 per level-up).</summary>
    public sealed class CharacterBuild
    {
        public ClassId Class;
        public int Level = 1;
        /// <summary>Free points spent per stat; total must not exceed 2 × (Level − 1).</summary>
        public PrimaryStats AllocatedPoints;

        public CharacterBuild() { }

        public CharacterBuild(ClassId classId, int level, PrimaryStats allocatedPoints)
        {
            Class = classId;
            Level = level;
            AllocatedPoints = allocatedPoints;
        }
    }
}
