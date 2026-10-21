using System;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Stats of a Normal-rank mob of level m (MOBS_NPC.md §2; reference table tables/mob_level_stats.csv).
    /// Rank multipliers (Elite/Guardian) are in <see cref="MobRules"/>; how they are rounded is left to the
    /// encounter/reward stages (S2/S3), the spec does not define it.
    /// </summary>
    public readonly struct MobLevelStats
    {
        public readonly int Level;
        public readonly int Hp;
        public readonly int Atk;
        public readonly int Def;
        public readonly int Exp;
        public readonly int GoldAvg;
        public readonly int GoldMin;
        public readonly int GoldMax;

        public MobLevelStats(int level, int hp, int atk, int def, int exp, int goldAvg, int goldMin, int goldMax)
        {
            Level = level; Hp = hp; Atk = atk; Def = def; Exp = exp; GoldAvg = goldAvg; GoldMin = goldMin; GoldMax = goldMax;
        }

        /// <summary>
        /// HP = round5(30 + 11 × m^1.3), Atk = round(6 + 3 × m^1.1), Def = round(4 + 2.2 × m),
        /// EXP = round(20 + 10 × m^1.15), talary avg = round(4 + 2 × m), min = max(1, round(avg × 0.7)),
        /// max = round(avg × 1.3).
        /// </summary>
        public static MobLevelStats ComputeNormal(BalanceConfig config, int level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (level < 1 || level > config.MaxMobLevel) throw new ArgumentOutOfRangeException(nameof(level), level, "Mob level must be 1.." + config.MaxMobLevel + ".");
            MobRules r = config.Mobs;
            int hp = GameplayMath.RoundTo(r.HpBase + r.HpScale * Math.Pow(level, r.HpExponent), r.HpRoundingStep);
            int atk = GameplayMath.RoundToInt(r.AtkBase + r.AtkScale * Math.Pow(level, r.AtkExponent));
            int def = GameplayMath.RoundToInt(r.DefBase + r.DefPerLevel * level);
            int exp = ExpCurve.MobExp(config.Exp, level);
            int goldAvg = GameplayMath.RoundToInt(r.GoldBase + r.GoldPerLevel * level);
            int goldMin = Math.Max(1, GameplayMath.RoundToInt(goldAvg * r.GoldMinFactor));
            int goldMax = GameplayMath.RoundToInt(goldAvg * r.GoldMaxFactor);
            return new MobLevelStats(level, hp, atk, def, exp, goldAvg, goldMin, goldMax);
        }
    }
}
