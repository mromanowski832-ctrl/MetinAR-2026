using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Skill rank rules, CLASSES_SKILLS.md §2 and tables/classes_skills.json "rankRules".
    /// Owner decision 2026-10-21 (max level 99, PR #61): ranks 1–10 from unlock, master ranks 11–15 need
    /// character level 60 / 68 / 76 / 84 / 92; 1 skill point per level-up from level 2.
    /// </summary>
    public sealed class SkillRankRules
    {
        public int MaxRank = 15;
        public int BaseMaxRank = 10;
        public double CoefPerRank = 0.08;
        public double MpCostPerRank = 0.06;
        public double CooldownReductionPerRank = 0.02;
        public double DurationPerRank = 0.03;
        public int SkillPointsPerLevelFromLevel2 = 1;

        /// <summary>Minimum character level for each master rank (key = rank 11…MaxRank).</summary>
        public Dictionary<int, int> MasterRankMinCharacterLevel = new Dictionary<int, int>
        {
            { 11, 60 }, { 12, 68 }, { 13, 76 }, { 14, 84 }, { 15, 92 },
        };

        /// <summary>Highest skill rank allowed at the given character level (assuming the skill is unlocked).</summary>
        public int MaxRankForCharacterLevel(int characterLevel)
        {
            if (characterLevel < 1) throw new ArgumentOutOfRangeException(nameof(characterLevel));
            int max = BaseMaxRank;
            for (int rank = BaseMaxRank + 1; rank <= MaxRank; rank++)
            {
                int required;
                if (!MasterRankMinCharacterLevel.TryGetValue(rank, out required))
                    throw new InvalidOperationException("Missing minimum character level for master rank " + rank + ".");
                if (characterLevel >= required) max = rank; else break;
            }
            return max;
        }

        /// <summary>Whether a skill unlocked at <paramref name="unlockLevel"/> may have <paramref name="rank"/> at this character level.</summary>
        public bool IsRankAllowed(int rank, int characterLevel, int unlockLevel)
        {
            if (rank < 1 || rank > MaxRank) return false;
            if (characterLevel < unlockLevel) return false;
            return rank <= MaxRankForCharacterLevel(characterLevel);
        }

        /// <summary>Total skill points earned by reaching <paramref name="characterLevel"/> (1 per level-up from level 2).</summary>
        public int SkillPointsAtLevel(int characterLevel)
        {
            if (characterLevel < 1) throw new ArgumentOutOfRangeException(nameof(characterLevel));
            return (characterLevel - 1) * SkillPointsPerLevelFromLevel2;
        }

        /// <summary>Damage/heal coefficient multiplier: 1 + 0.08 × (rank − 1).</summary>
        public double CoefMultiplier(int rank) { return 1.0 + CoefPerRank * (CheckRank(rank) - 1); }

        /// <summary>MP cost multiplier: 1 + 0.06 × (rank − 1).</summary>
        public double MpCostMultiplier(int rank) { return 1.0 + MpCostPerRank * (CheckRank(rank) - 1); }

        /// <summary>Cooldown multiplier: 1 − 0.02 × (rank − 1).</summary>
        public double CooldownMultiplier(int rank) { return 1.0 - CooldownReductionPerRank * (CheckRank(rank) - 1); }

        /// <summary>
        /// Effect duration multiplier: "+3% na rangę", applied like the other rank rules from rank 1:
        /// 1 + 0.03 × (rank − 1).
        /// </summary>
        public double DurationMultiplier(int rank) { return 1.0 + DurationPerRank * (CheckRank(rank) - 1); }

        private int CheckRank(int rank)
        {
            if (rank < 1 || rank > MaxRank) throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank must be 1.." + MaxRank + ".");
            return rank;
        }
    }
}
