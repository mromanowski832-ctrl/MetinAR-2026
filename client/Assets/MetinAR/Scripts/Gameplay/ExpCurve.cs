using System;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Character EXP curve (CHARACTER_PROGRESSION.md §2), levels 1…MaxCharacterLevel (99):
    /// ExpToNext(L) = round10(MobExp(L) × K(L)), MobExp(m) = round(20 + 10 × m^1.15), K(L) = 4 + 1.2 × L.
    /// The max level has ExpToNext = 0. Values are precomputed once; tables/exp_curve.csv is the reference.
    /// </summary>
    public sealed class ExpCurve
    {
        private readonly ExpRules _rules;
        private readonly long[] _expToNext;   // index = level
        private readonly long[] _cumulative;  // total EXP at the start of level

        public int MaxLevel { get; }

        public ExpCurve(BalanceConfig config) : this(config.Exp, config.MaxCharacterLevel, null) { }

        private ExpCurve(ExpRules rules, int maxLevel, long[] expToNextOverride)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (maxLevel < 2) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            _rules = rules;
            MaxLevel = maxLevel;
            _expToNext = new long[maxLevel + 1];
            _cumulative = new long[maxLevel + 1];
            long sum = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                long next = level == maxLevel ? 0 : (expToNextOverride != null ? expToNextOverride[level] : FormulaExpToNext(rules, level));
                if (level < maxLevel && next <= 0) throw new ArgumentException("ExpToNext must be positive below max level (level " + level + ").");
                _expToNext[level] = next;
                _cumulative[level] = sum;
                sum = checked(sum + next);
            }
        }

        /// <summary>
        /// Builds a curve from imported "expToNext" values (index = level, entries 1…maxLevel−1 used),
        /// e.g. from tables/exp_curve.csv.
        /// </summary>
        public static ExpCurve FromTable(ExpRules rules, int maxLevel, long[] expToNextByLevel)
        {
            if (expToNextByLevel == null) throw new ArgumentNullException(nameof(expToNextByLevel));
            if (expToNextByLevel.Length < maxLevel) throw new ArgumentException("Table must cover levels 1.." + (maxLevel - 1) + ".");
            return new ExpCurve(rules, maxLevel, expToNextByLevel);
        }

        /// <summary>Same-level mob EXP: round(20 + 10 × m^1.15).</summary>
        public static int MobExp(ExpRules rules, int mobLevel)
        {
            if (mobLevel < 1) throw new ArgumentOutOfRangeException(nameof(mobLevel));
            return GameplayMath.RoundToInt(rules.MobExpBase + rules.MobExpScale * Math.Pow(mobLevel, rules.MobExpExponent));
        }

        /// <summary>K(L) = 4 + 1.2 × L.</summary>
        public static double KillEquivalents(ExpRules rules, int level)
        {
            return rules.KillEquivalentsBase + rules.KillEquivalentsPerLevel * level;
        }

        public static long FormulaExpToNext(ExpRules rules, int level)
        {
            return GameplayMath.RoundTo(MobExp(rules, level) * KillEquivalents(rules, level), rules.ExpToNextRoundingStep);
        }

        public int MobExp(int mobLevel) { return MobExp(_rules, mobLevel); }

        public double KillEquivalents(int level) { return KillEquivalents(_rules, level); }

        /// <summary>EXP needed to go from <paramref name="level"/> to the next one (0 at max level).</summary>
        public long ExpToNext(int level) { return _expToNext[CheckLevel(level)]; }

        /// <summary>Total EXP at the start of <paramref name="level"/> (level 1 = 0).</summary>
        public long CumulativeExpAtLevelStart(int level) { return _cumulative[CheckLevel(level)]; }

        public long TotalExpToMaxLevel { get { return _cumulative[MaxLevel]; } }

        /// <summary>EXP per HUD orb (4 orbs × 25%): floor(ExpToNext / 4), as in exp_curve.csv.</summary>
        public long ExpPerHudOrb(int level) { return ExpToNext(level) / _rules.HudOrbCount; }

        /// <summary>Level reached with <paramref name="totalExp"/> (capped at max level).</summary>
        public int LevelForTotalExp(long totalExp)
        {
            if (totalExp < 0) throw new ArgumentOutOfRangeException(nameof(totalExp));
            int lo = 1, hi = MaxLevel;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_cumulative[mid] <= totalExp) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>EXP gained inside the current level for a total EXP value.</summary>
        public long ExpIntoLevel(long totalExp)
        {
            return totalExp - _cumulative[LevelForTotalExp(totalExp)];
        }

        /// <summary>Progress in the current level, 0…1 (1 at max level).</summary>
        public double LevelProgress(long totalExp)
        {
            int level = LevelForTotalExp(totalExp);
            long next = _expToNext[level];
            if (next == 0) return 1.0;
            return GameplayMath.Clamp01((double)(totalExp - _cumulative[level]) / next);
        }

        /// <summary>Filled HUD orbs as a fraction 0…4 (CHARACTER_PROGRESSION.md §2.1, fractional fill).</summary>
        public double FilledHudOrbs(long totalExp)
        {
            return LevelProgress(totalExp) * _rules.HudOrbCount;
        }

        /// <summary>
        /// EXP multiplier for the mob/player level difference (§2.2): ≥ 5 above ×1.25, 1–4 above +5%/level,
        /// equal to 5 below ×1.0, more than 5 below −15% per level beyond 5, minimum ×0.10.
        /// </summary>
        public static double LevelDifferenceMultiplier(ExpRules rules, int mobLevel, int playerLevel)
        {
            int diff = mobLevel - playerLevel;
            if (diff >= rules.LevelDiffCapAbove) return rules.LevelDiffMultiplierCap;
            if (diff > 0) return 1.0 + rules.LevelDiffBonusPerLevelAbove * diff;
            int below = -diff;
            if (below <= rules.LevelDiffNeutralBelow) return 1.0;
            return Math.Max(rules.LevelDiffMultiplierFloor, 1.0 - rules.LevelDiffPenaltyPerLevelBelow * (below - rules.LevelDiffNeutralBelow));
        }

        public double LevelDifferenceMultiplier(int mobLevel, int playerLevel)
        {
            return LevelDifferenceMultiplier(_rules, mobLevel, playerLevel);
        }

        private int CheckLevel(int level)
        {
            if (level < 1 || level > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be 1.." + MaxLevel + ".");
            return level;
        }
    }
}
