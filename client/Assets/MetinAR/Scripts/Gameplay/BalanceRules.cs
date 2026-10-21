using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay
{
    // All defaults below are the starting values approved with the METINAR-017 spec (PR #44, owner 2026-10-21)
    // with the owner's max level 99 (PR #61). They are balancing start points (S12 tunes them), not final balance.
    // Source section is noted on every group; do not change a value here without the matching docs/design change (#41).

    /// <summary>Derived stat formulas, CHARACTER_PROGRESSION.md §1.3–1.4.</summary>
    public sealed class DerivedStatRules
    {
        public double BaseHp = 60;
        public double HpPerVigor = 6;
        public double BaseMp = 20;
        public double MpPerSpirit = 5;
        public double AtkPerScalingStat = 0.8;
        public double AtkPerLevel = 0.5;
        public double MagPerSpirit = 1.0;
        public double MagPerLevel = 0.5;
        public double DefPerVigor = 0.8;
        public double BaseAccuracy = 70;
        public double AccuracyPerFinesse = 1.0;
        public double AccuracyPerLevel = 1.0;
        public double BaseEvasion = 5;
        public double EvasionPerFinesse = 0.5;
        public double BaseCritChance = 0.03;
        public double CritChancePerFinesse = 0.0015;
        public double CritChanceCap = 0.40;
        public double BaseCritMultiplier = 1.6;
        public double CritMultiplierCap = 2.2;
        public double BlockChanceCap = 0.35;
        public double ResistanceCap = 0.50;
        /// <summary>Out of combat: 2% MaxHp every 5 s.</summary>
        public double HpRegenOutOfCombatFractionPer5s = 0.02;
        public double MpRegenBasePerSecond = 1.0;
        public double MpRegenPerSpiritPerSecond = 0.05;
        public double MpRegenOutOfCombatMultiplier = 3.0;
        /// <summary>Base swing from the FPP prototype (#31), seconds.</summary>
        public double BaseSwingSeconds = 0.72;
        public double AttackSpeedModifierMin = 0.80;
        public double AttackSpeedModifierMax = 1.25;
        /// <summary>Plain weapon: WeaponAtk(iL) = 4 + 1.6 × iL (before rarity multiplier).</summary>
        public double WeaponAtkBase = 4;
        public double WeaponAtkPerItemLevel = 1.6;
    }

    /// <summary>Damage resolution constants, COMBAT_PHYSICS.md §1.</summary>
    public sealed class DamageRules
    {
        public double HitChanceBase = 0.92;
        public double HitChancePerAccuracyPoint = 0.004;
        public double HitChancePenaltyPerTargetLevelAbove = 0.02;
        public double HitChanceMin = 0.40;
        public double HitChanceMax = 0.99;
        /// <summary>Blocked hit = −60% damage.</summary>
        public double BlockDamageReduction = 0.60;
        public double VarianceMin = 0.92;
        public double VarianceMax = 1.08;
        public double MitigationConstant = 50;
        public double MitigationPerAttackerLevel = 8;
        /// <summary>Magical damage uses half of Def.</summary>
        public double MagicalDefenseFactor = 0.5;
        public int MinimumDamage = 1;
    }

    /// <summary>EXP curve and EXP modifiers, CHARACTER_PROGRESSION.md §2 and §3.</summary>
    public sealed class ExpRules
    {
        /// <summary>MobExp(m) = round(20 + 10 × m^1.15).</summary>
        public double MobExpBase = 20;
        public double MobExpScale = 10;
        public double MobExpExponent = 1.15;
        /// <summary>K(L) = 4 + 1.2 × L same-level kill equivalents.</summary>
        public double KillEquivalentsBase = 4;
        public double KillEquivalentsPerLevel = 1.2;
        /// <summary>ExpToNext is rounded to a multiple of 10 (round10).</summary>
        public int ExpToNextRoundingStep = 10;
        /// <summary>HUD shows 4 orbs, 25% of the level each (§2.1).</summary>
        public int HudOrbCount = 4;

        // §2.2 level difference modifier (mob level − player level).
        public int LevelDiffCapAbove = 5;
        public double LevelDiffMultiplierCap = 1.25;
        public double LevelDiffBonusPerLevelAbove = 0.05;
        public int LevelDiffNeutralBelow = 5;
        public double LevelDiffPenaltyPerLevelBelow = 0.15;
        public double LevelDiffMultiplierFloor = 0.10;
    }

    public enum MobRank
    {
        Normal = 0,
        Elite = 1,
        Guardian = 2,
    }

    /// <summary>Normal mob stats by level and rank multipliers, MOBS_NPC.md §2.</summary>
    public sealed class MobRules
    {
        /// <summary>HP = round5(30 + 11 × m^1.3).</summary>
        public double HpBase = 30;
        public double HpScale = 11;
        public double HpExponent = 1.3;
        public int HpRoundingStep = 5;
        /// <summary>Atk = round(6 + 3 × m^1.1).</summary>
        public double AtkBase = 6;
        public double AtkScale = 3.0;
        public double AtkExponent = 1.1;
        /// <summary>Def = round(4 + 2.2 × m).</summary>
        public double DefBase = 4;
        public double DefPerLevel = 2.2;
        /// <summary>Talary: average round(4 + 2 × m), range ×0.7…×1.3 (min 1).</summary>
        public double GoldBase = 4;
        public double GoldPerLevel = 2.0;
        public double GoldMinFactor = 0.7;
        public double GoldMaxFactor = 1.3;

        public double EliteHpMultiplier = 3.5;
        public double EliteAtkMultiplier = 1.4;
        public double EliteRewardMultiplier = 3;
        public double GuardianHpMultiplier = 12;
        public double GuardianAtkMultiplier = 1.8;
        public double GuardianRewardMultiplier = 10;

        public double HpMultiplier(MobRank rank)
        {
            switch (rank)
            {
                case MobRank.Normal: return 1;
                case MobRank.Elite: return EliteHpMultiplier;
                case MobRank.Guardian: return GuardianHpMultiplier;
                default: throw new ArgumentOutOfRangeException(nameof(rank), rank, null);
            }
        }

        public double AtkMultiplier(MobRank rank)
        {
            switch (rank)
            {
                case MobRank.Normal: return 1;
                case MobRank.Elite: return EliteAtkMultiplier;
                case MobRank.Guardian: return GuardianAtkMultiplier;
                default: throw new ArgumentOutOfRangeException(nameof(rank), rank, null);
            }
        }

        /// <summary>EXP and talary multiplier of the rank (×1 / ×3 / ×10).</summary>
        public double RewardMultiplier(MobRank rank)
        {
            switch (rank)
            {
                case MobRank.Normal: return 1;
                case MobRank.Elite: return EliteRewardMultiplier;
                case MobRank.Guardian: return GuardianRewardMultiplier;
                default: throw new ArgumentOutOfRangeException(nameof(rank), rank, null);
            }
        }
    }

    /// <summary>Rarity tier (ITEMS_COMBAT_SPEC.md §3, tables/rarity_tiers.csv).</summary>
    public sealed class RarityTier
    {
        public string Id;
        public int AffixCount;
        public double BaseStatMultiplier;

        public RarityTier(string id, int affixCount, double baseStatMultiplier)
        {
            Id = id;
            AffixCount = affixCount;
            BaseStatMultiplier = baseStatMultiplier;
        }
    }

    /// <summary>Temper level +1…+10 (ITEMS_COMBAT_SPEC.md §4, tables/temper_levels.csv).</summary>
    public sealed class TemperLevel
    {
        public int Level;
        /// <summary>Bonus to base stats as a fraction (6% = 0.06).</summary>
        public double BaseStatBonus;
        public double SuccessChance;
        /// <summary>"nothing" or "drop_one_level"; the item is never destroyed.</summary>
        public string OnFail;
        public int OreShards;

        public TemperLevel(int level, double baseStatBonus, double successChance, string onFail, int oreShards)
        {
            Level = level;
            BaseStatBonus = baseStatBonus;
            SuccessChance = successChance;
            OnFail = onFail;
            OreShards = oreShards;
        }
    }

    internal static class DefaultTables
    {
        public static List<RarityTier> Rarities()
        {
            return new List<RarityTier>
            {
                new RarityTier("common", 0, 1.00),
                new RarityTier("uncommon", 1, 1.06),
                new RarityTier("rare", 2, 1.12),
                new RarityTier("epic", 3, 1.20),
                new RarityTier("legendary", 3, 1.30),
            };
        }

        public static List<TemperLevel> TemperLevels()
        {
            return new List<TemperLevel>
            {
                new TemperLevel(1, 0.06, 1.00, "nothing", 1),
                new TemperLevel(2, 0.12, 1.00, "nothing", 2),
                new TemperLevel(3, 0.18, 0.95, "nothing", 2),
                new TemperLevel(4, 0.24, 0.90, "nothing", 3),
                new TemperLevel(5, 0.30, 0.80, "nothing", 4),
                new TemperLevel(6, 0.36, 0.70, "drop_one_level", 5),
                new TemperLevel(7, 0.42, 0.60, "drop_one_level", 6),
                new TemperLevel(8, 0.48, 0.50, "drop_one_level", 8),
                new TemperLevel(9, 0.54, 0.40, "drop_one_level", 10),
                new TemperLevel(10, 0.60, 0.30, "drop_one_level", 12),
            };
        }
    }
}
