using System;

namespace MetinAR.Gameplay.Combat
{
    /// <summary>
    /// Combat-relevant stats of one mob in an AR encounter (METINAR-020 / S2). Built from the normal-mob formulas of
    /// MOBS_NPC.md §2 (<see cref="MobLevelStats"/>) and the rank multipliers. EXP and talary are kept at the Normal
    /// value; the rank reward multiplier is applied by <see cref="MobRewardCalculator"/>.
    /// </summary>
    public sealed class MobCombatProfile
    {
        public const string VenomousSpiderId = "mob_venomous_spider";

        public string Id { get; }
        public int Level { get; }
        public MobRank Rank { get; }
        public int MaxHp { get; }
        public int Atk { get; }
        public int Def { get; }
        public double Evasion { get; }
        public double Accuracy { get; }
        /// <summary>Weak point bonus added to the attacker's crit chance (spider eyes/head +0.15).</summary>
        public double WeakPointCritBonus { get; }
        /// <summary>Normal-rank EXP of the level (before rank and level-difference multipliers).</summary>
        public int BaseExp { get; }
        /// <summary>Normal-rank talary range of the level (before the rank multiplier), inclusive.</summary>
        public int TalaryMin { get; }
        public int TalaryMax { get; }

        public MobCombatProfile(string id, int level, MobRank rank, int maxHp, int atk, int def, double evasion, double accuracy,
            double weakPointCritBonus, int baseExp, int talaryMin, int talaryMax)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Mob id is required.", nameof(id));
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (maxHp < 1) throw new ArgumentOutOfRangeException(nameof(maxHp));
            if (talaryMin < 0 || talaryMax < talaryMin) throw new ArgumentOutOfRangeException(nameof(talaryMax));
            Id = id; Level = level; Rank = rank; MaxHp = maxHp; Atk = atk; Def = def; Evasion = evasion; Accuracy = accuracy;
            WeakPointCritBonus = weakPointCritBonus; BaseExp = baseExp; TalaryMin = talaryMin; TalaryMax = talaryMax;
        }

        /// <summary>
        /// Mob of level <paramref name="level"/> and <paramref name="rank"/>: HP = round5(normal HP × rank HP multiplier),
        /// Atk = round(normal Atk × rank Atk multiplier), Def unchanged; Eva/Acc from <see cref="MobRules.Evasion"/> /
        /// <see cref="MobRules.AccuracyBase"/> (S2 decision in #41).
        /// </summary>
        public static MobCombatProfile ForLevel(BalanceConfig config, string id, int level, MobRank rank, double weakPointCritBonus)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            MobLevelStats normal = MobLevelStats.ComputeNormal(config, level);
            MobRules r = config.Mobs;
            int hp = rank == MobRank.Normal ? normal.Hp : GameplayMath.RoundTo(normal.Hp * r.HpMultiplier(rank), r.HpRoundingStep);
            int atk = rank == MobRank.Normal ? normal.Atk : GameplayMath.RoundToInt(normal.Atk * r.AtkMultiplier(rank));
            double accuracy = r.AccuracyBase + r.AccuracyPerLevel * level;
            return new MobCombatProfile(id, level, rank, hp, atk, normal.Def, r.Evasion, accuracy, weakPointCritBonus,
                normal.Exp, normal.GoldMin, normal.GoldMax);
        }

        /// <summary>Trujący Pająk of the starter kit (MOBS_NPC.md §3): level 5, Normal, weak point +15 pp crit.</summary>
        public static MobCombatProfile VenomousSpider(BalanceConfig config, int level = 5)
        {
            return ForLevel(config, VenomousSpiderId, level, MobRank.Normal, 0.15);
        }
    }
}
