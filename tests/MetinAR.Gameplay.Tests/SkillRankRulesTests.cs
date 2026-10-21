using System;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class SkillRankRulesTests
    {
        private static readonly SkillRankRules Rules = BalanceConfig.CreateDefault().SkillRanks;

        [Theory]
        [InlineData(1, 10)]
        [InlineData(59, 10)]
        [InlineData(60, 11)]
        [InlineData(67, 11)]
        [InlineData(68, 12)]
        [InlineData(76, 13)]
        [InlineData(84, 14)]
        [InlineData(91, 14)]
        [InlineData(92, 15)]
        [InlineData(99, 15)]
        public void MaxRankForCharacterLevel_MasterRanksFrom60(int level, int expectedMaxRank)
        {
            Assert.Equal(expectedMaxRank, Rules.MaxRankForCharacterLevel(level));
        }

        [Fact]
        public void IsRankAllowed_RespectsUnlockLevelAndMasterRanks()
        {
            Assert.False(Rules.IsRankAllowed(1, 9, 10));
            Assert.True(Rules.IsRankAllowed(1, 10, 10));
            Assert.True(Rules.IsRankAllowed(10, 10, 10));
            Assert.False(Rules.IsRankAllowed(11, 59, 1));
            Assert.True(Rules.IsRankAllowed(11, 60, 1));
            Assert.True(Rules.IsRankAllowed(15, 99, 34));
            Assert.False(Rules.IsRankAllowed(16, 99, 1));
            Assert.False(Rules.IsRankAllowed(0, 99, 1));
        }

        [Fact]
        public void SkillPoints_OnePerLevelUp()
        {
            Assert.Equal(0, Rules.SkillPointsAtLevel(1));
            Assert.Equal(59, Rules.SkillPointsAtLevel(60));
            Assert.Equal(98, Rules.SkillPointsAtLevel(99));
        }

        [Fact]
        public void RankScaling()
        {
            Assert.Equal(1.0, Rules.CoefMultiplier(1), 12);
            Assert.Equal(1.72, Rules.CoefMultiplier(10), 12);
            Assert.Equal(2.12, Rules.CoefMultiplier(15), 12);
            Assert.Equal(1.54, Rules.MpCostMultiplier(10), 12);
            Assert.Equal(0.82, Rules.CooldownMultiplier(10), 12);
            Assert.Equal(0.72, Rules.CooldownMultiplier(15), 12);
            Assert.Equal(1.27, Rules.DurationMultiplier(10), 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules.CoefMultiplier(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules.CoefMultiplier(16));
        }
    }
}
