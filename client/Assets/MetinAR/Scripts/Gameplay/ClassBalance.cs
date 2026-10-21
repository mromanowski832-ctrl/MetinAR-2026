using System;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// Per-class values from CHARACTER_PROGRESSION.md §1.2: starting primary stats, the two stats that get the
    /// automatic +1 per level-up, and HP/MP per level.
    /// </summary>
    public sealed class ClassBalance
    {
        public ClassId Id { get; }
        public PrimaryStats StartingStats { get; }
        public PrimaryStat AutoGrowthA { get; }
        public PrimaryStat AutoGrowthB { get; }
        public int HpPerLevel { get; }
        public int MpPerLevel { get; }

        public ClassBalance(ClassId id, PrimaryStats startingStats, PrimaryStat autoGrowthA, PrimaryStat autoGrowthB, int hpPerLevel, int mpPerLevel)
        {
            if (autoGrowthA == autoGrowthB) throw new ArgumentException("Auto-growth stats must differ.");
            if (hpPerLevel < 0 || mpPerLevel < 0) throw new ArgumentOutOfRangeException(nameof(hpPerLevel));
            Id = id;
            StartingStats = startingStats;
            AutoGrowthA = autoGrowthA;
            AutoGrowthB = autoGrowthB;
            HpPerLevel = hpPerLevel;
            MpPerLevel = mpPerLevel;
        }

        /// <summary>Starting stats plus the automatic class growth (+1 in two stats for every level-up, i.e. L − 1 times).</summary>
        public PrimaryStats AutoStatsAtLevel(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            int ups = level - 1;
            return StartingStats.Add(AutoGrowthA, ups).Add(AutoGrowthB, ups);
        }
    }
}
