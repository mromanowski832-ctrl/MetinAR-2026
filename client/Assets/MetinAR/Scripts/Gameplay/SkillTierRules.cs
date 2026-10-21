using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// One skill rank (rankIndex 1–29) of the tier system Wprawa → Biegłość → Kunszt → Szczyt
    /// (CLASSES_SKILLS.md §2.1, owner decision 2026-10-21 10:13; reference table tables/skill_tiers.csv).
    /// </summary>
    public sealed class SkillTierDefinition
    {
        public int RankIndex;
        /// <summary>practice (Wprawa), proficiency (Biegłość), artistry (Kunszt), apex (Szczyt).</summary>
        public string TierId;
        public int TierRank;
        public string LabelPl;
        /// <summary>skill_point, skill_point_auto_promotion, treatise_proficiency_&lt;classId&gt;, spark_artistry, seal_apex.</summary>
        public string RaisedBy;
        public int SkillPointCost;
        /// <summary>Attempt success chance 0…1 (1 for ranks raised with skill points).</summary>
        public double SuccessChance;
        public double AttemptCooldownHours;
        /// <summary>EXP cost of one attempt as a fraction of the character's ExpToNext (0.03 = 3%).</summary>
        public double ExpCostFractionOfExpToNext;
        public int MinCharacterLevel;
        public double CoefMult;
        public double MpCostMult;
        public double CooldownMult;
        public double DurationMult;
    }

    /// <summary>
    /// Skill rank rules (CLASSES_SKILLS.md §2 and §2.1). Ranks 1–15 cost 1 skill point each (15th = automatic
    /// promotion to Biegłość I); 16–29 are raised with tier items by chance and cooldown, decided by the server
    /// (attempts are not implemented in S1). 1 skill point per level-up from level 2.
    /// </summary>
    public sealed class SkillTierRules
    {
        public int SkillPointsPerLevelFromLevel2 = 1;
        public List<SkillTierDefinition> Ranks = CreateDefaultRanks();

        public int MaxRankIndex { get { return Ranks.Count; } }

        public SkillTierDefinition Get(int rankIndex)
        {
            if (rankIndex < 1 || rankIndex > Ranks.Count)
                throw new ArgumentOutOfRangeException(nameof(rankIndex), rankIndex, "Rank index must be 1.." + Ranks.Count + ".");
            return Ranks[rankIndex - 1];
        }

        /// <summary>Total skill points earned by reaching <paramref name="characterLevel"/> (1 per level-up from level 2).</summary>
        public int SkillPointsAtLevel(int characterLevel)
        {
            if (characterLevel < 1) throw new ArgumentOutOfRangeException(nameof(characterLevel));
            return (characterLevel - 1) * SkillPointsPerLevelFromLevel2;
        }

        /// <summary>Skill points spent on one skill to reach <paramref name="rankIndex"/> (rank 15 = 15 points).</summary>
        public int SkillPointsToReach(int rankIndex)
        {
            Get(rankIndex);
            int sum = 0;
            for (int i = 0; i < rankIndex; i++) sum += Ranks[i].SkillPointCost;
            return sum;
        }

        /// <summary>Highest rank reachable with skill points only (Biegłość I).</summary>
        public int MaxRankBySkillPoints
        {
            get
            {
                int max = 0;
                for (int i = 0; i < Ranks.Count && Ranks[i].SkillPointCost > 0; i++) max = Ranks[i].RankIndex;
                return max;
            }
        }

        /// <summary>Character level requirement of the rank (skill unlock level is checked separately).</summary>
        public bool IsRankAllowedAtLevel(int rankIndex, int characterLevel)
        {
            return characterLevel >= Get(rankIndex).MinCharacterLevel;
        }

        public double CoefMultiplier(int rankIndex) { return Get(rankIndex).CoefMult; }
        public double MpCostMultiplier(int rankIndex) { return Get(rankIndex).MpCostMult; }
        public double CooldownMultiplier(int rankIndex) { return Get(rankIndex).CooldownMult; }
        public double DurationMultiplier(int rankIndex) { return Get(rankIndex).DurationMult; }

        // --- Defaults: formulas and per-rank values of CLASSES_SKILLS.md §2/§2.1 (same as generate_tables.py). ---

        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };
        private static readonly int[] TreatiseChancePct = { 70, 65, 60, 55, 50, 45, 40 };   // Biegłość II…VIII
        private static readonly int[] SparkChancePct = { 35, 30, 26, 22, 18, 15 };          // Kunszt I…VI
        private static readonly int[] SparkMinLevel = { 60, 66, 72, 78, 84, 90 };

        /// <summary>coefMult: Wprawa 1 + 0.06 × (r − 1); Biegłość 1.84 + 0.07 × (r − 15); Kunszt 2.45 + 0.10 × (r − 23); Szczyt 3.30.</summary>
        public static double DefaultCoefMult(int rankIndex)
        {
            if (rankIndex <= 14) return 1 + 0.06 * (rankIndex - 1);
            if (rankIndex <= 22) return 1.84 + 0.07 * (rankIndex - 15);
            if (rankIndex <= 28) return 2.45 + 0.10 * (rankIndex - 23);
            return 3.30;
        }

        public static List<SkillTierDefinition> CreateDefaultRanks()
        {
            var ranks = new List<SkillTierDefinition>();
            for (int r = 1; r <= 14; r++)
                ranks.Add(Rank("practice", "Wprawa " + r, r, "skill_point", 1, 100, 0, 0, 1));
            ranks.Add(Rank("proficiency", "Biegłość I", 1, "skill_point_auto_promotion", 1, 100, 0, 0, 1));
            for (int k = 2; k <= 8; k++)
                ranks.Add(Rank("proficiency", "Biegłość " + Roman[k - 1], k, "treatise_proficiency_<classId>", 0, TreatiseChancePct[k - 2], 6, 3, 20));
            for (int k = 1; k <= 6; k++)
                ranks.Add(Rank("artistry", "Kunszt " + Roman[k - 1], k, "spark_artistry", 0, SparkChancePct[k - 1], 24, 6, SparkMinLevel[k - 1]));
            ranks.Add(Rank("apex", "Szczyt", 1, "seal_apex", 0, 12, 72, 10, 95));
            for (int i = 0; i < ranks.Count; i++)
            {
                int idx = i + 1;
                var d = ranks[i];
                d.RankIndex = idx;
                d.CoefMult = DefaultCoefMult(idx);
                d.MpCostMult = 1 + 0.04 * (idx - 1);
                d.CooldownMult = Math.Max(0.65, 1 - 0.0125 * (idx - 1));
                d.DurationMult = 1 + 0.025 * (idx - 1);
            }
            return ranks;
        }

        private static SkillTierDefinition Rank(string tierId, string label, int tierRank, string raisedBy, int pointCost,
            int chancePct, double cooldownHours, int expPct, int minLevel)
        {
            return new SkillTierDefinition
            {
                TierId = tierId, LabelPl = label, TierRank = tierRank, RaisedBy = raisedBy, SkillPointCost = pointCost,
                SuccessChance = chancePct / 100.0, AttemptCooldownHours = cooldownHours,
                ExpCostFractionOfExpToNext = expPct / 100.0, MinCharacterLevel = minLevel,
            };
        }
    }
}
