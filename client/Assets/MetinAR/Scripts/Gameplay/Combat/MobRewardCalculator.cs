using System;

namespace MetinAR.Gameplay.Combat
{
    /// <summary>EXP and talary for one defeated mob.</summary>
    public readonly struct MobKillReward
    {
        public readonly int Exp;
        public readonly int Talary;
        /// <summary>Level-difference EXP multiplier used (CHARACTER_PROGRESSION.md §2.2).</summary>
        public readonly double LevelDifferenceMultiplier;
        /// <summary>Rank multiplier used for EXP and talary (×1 / ×3 / ×10).</summary>
        public readonly double RankMultiplier;

        public MobKillReward(int exp, int talary, double levelDifferenceMultiplier, double rankMultiplier)
        {
            Exp = exp; Talary = talary; LevelDifferenceMultiplier = levelDifferenceMultiplier; RankMultiplier = rankMultiplier;
        }
    }

    /// <summary>
    /// Kill rewards (CHARACTER_PROGRESSION.md §3, ECONOMY.md §2). Rounding rule decided for S2 in #41 (spec default
    /// proposals, same rounding as the table generator):
    /// EXP = max(1, round(MobExp(m) × rank × levelDiff)) — one rounding at the end, ties to even;
    /// talary = max(1, round(U{min…max} × rank)) — integer roll from the table range, the level-difference
    /// modifier is EXP-only (§2.2), the rank multiplier applies to both (×1 / ×3 / ×10).
    /// Rewards are granted locally in v1; the server takes over in S11 (#56).
    /// </summary>
    public static class MobRewardCalculator
    {
        public static int Exp(BalanceConfig config, MobCombatProfile mob, int playerLevel)
        {
            double levelDiff;
            return Exp(config, mob, playerLevel, out levelDiff);
        }

        private static int Exp(BalanceConfig config, MobCombatProfile mob, int playerLevel, out double levelDiff)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (mob == null) throw new ArgumentNullException(nameof(mob));
            config.CheckCharacterLevel(playerLevel);
            levelDiff = ExpCurve.LevelDifferenceMultiplier(config.Exp, mob.Level, playerLevel);
            double exp = mob.BaseExp * config.Mobs.RewardMultiplier(mob.Rank) * levelDiff;
            return Math.Max(1, GameplayMath.RoundToInt(exp));
        }

        /// <summary>Uniform integer talary roll in [TalaryMin, TalaryMax] × rank multiplier. Uses one random draw.</summary>
        public static int RollTalary(BalanceConfig config, MobCombatProfile mob, IGameplayRandom random)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (mob == null) throw new ArgumentNullException(nameof(mob));
            if (random == null) throw new ArgumentNullException(nameof(random));
            int span = mob.TalaryMax - mob.TalaryMin + 1;
            int offset = Math.Min(span - 1, (int)Math.Floor(random.NextDouble() * span));
            int roll = mob.TalaryMin + offset;
            return Math.Max(1, GameplayMath.RoundToInt(roll * config.Mobs.RewardMultiplier(mob.Rank)));
        }

        /// <summary>EXP for <paramref name="playerLevel"/> and one talary roll.</summary>
        public static MobKillReward Roll(BalanceConfig config, MobCombatProfile mob, int playerLevel, IGameplayRandom random)
        {
            double levelDiff;
            int exp = Exp(config, mob, playerLevel, out levelDiff);
            int talary = RollTalary(config, mob, random);
            return new MobKillReward(exp, talary, levelDiff, config.Mobs.RewardMultiplier(mob.Rank));
        }
    }
}
