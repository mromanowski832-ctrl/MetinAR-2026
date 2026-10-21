using System;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class SkillTierRulesTests
    {
        private static readonly SkillTierRules Tiers = BalanceConfig.CreateDefault().SkillTiers;

        [Fact]
        public void Defaults_MatchSkillTiersTable()
        {
            var table = BalanceTableImporter.ReadSkillTiers(TestData.RepoTable("skill_tiers.csv"));
            Assert.Equal(29, table.Count);
            Assert.Equal(29, Tiers.MaxRankIndex);
            for (int i = 0; i < table.Count; i++)
            {
                var e = table[i];
                var a = Tiers.Ranks[i];
                string at = " (rank " + e.RankIndex + ")";
                Assert.True(e.RankIndex == a.RankIndex, "rankIndex" + at);
                Assert.True(e.TierId == a.TierId, "tierId" + at);
                Assert.True(e.TierRank == a.TierRank, "tierRank" + at);
                Assert.True(e.LabelPl == a.LabelPl, "label" + at);
                Assert.True(e.RaisedBy == a.RaisedBy, "raisedBy" + at);
                Assert.True(e.SkillPointCost == a.SkillPointCost, "skillPointCost" + at);
                Assert.True(Math.Abs(e.SuccessChance - a.SuccessChance) < 1e-9, "chance" + at);
                Assert.True(Math.Abs(e.AttemptCooldownHours - a.AttemptCooldownHours) < 1e-9, "cooldownH" + at);
                Assert.True(Math.Abs(e.ExpCostFractionOfExpToNext - a.ExpCostFractionOfExpToNext) < 1e-9, "expCost" + at);
                Assert.True(e.MinCharacterLevel == a.MinCharacterLevel, "minLevel" + at);
                // Table values are rounded for display: coef/mp to 0.01, cooldown to 0.0001, duration to 0.001.
                Assert.True(Math.Abs(e.CoefMult - a.CoefMult) <= 0.005 + 1e-9, "coefMult" + at);
                Assert.True(Math.Abs(e.MpCostMult - a.MpCostMult) <= 0.005 + 1e-9, "mpCostMult" + at);
                Assert.True(Math.Abs(e.CooldownMult - a.CooldownMult) <= 0.00005 + 1e-9, "cooldownMult" + at);
                Assert.True(Math.Abs(e.DurationMult - a.DurationMult) <= 0.0005 + 1e-9, "durationMult" + at);
            }
        }

        [Theory]
        // CLASSES_SKILLS.md §2: Wprawa 1.00…1.78, Biegłość I–VIII 1.84…2.33, Kunszt I–VI 2.45…2.95, Szczyt 3.30.
        [InlineData(1, 1.00)]
        [InlineData(14, 1.78)]
        [InlineData(15, 1.84)]
        [InlineData(22, 2.33)]
        [InlineData(23, 2.45)]
        [InlineData(28, 2.95)]
        [InlineData(29, 3.30)]
        public void CoefMultiplier_SpecAnchors(int rankIndex, double expected)
        {
            Assert.Equal(expected, Tiers.CoefMultiplier(rankIndex), 9);
        }

        [Fact]
        public void ApexMultipliers()
        {
            Assert.Equal(2.12, Tiers.MpCostMultiplier(29), 9);
            Assert.Equal(0.65, Tiers.CooldownMultiplier(29), 9);
            Assert.Equal(1.70, Tiers.DurationMultiplier(29), 9);
            Assert.Throws<ArgumentOutOfRangeException>(() => Tiers.CoefMultiplier(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tiers.CoefMultiplier(30));
        }

        [Fact]
        public void SkillPoints_OnePerLevelUp_15PointsToProficiencyI()
        {
            Assert.Equal(0, Tiers.SkillPointsAtLevel(1));
            Assert.Equal(98, Tiers.SkillPointsAtLevel(99));
            Assert.Equal(15, Tiers.MaxRankBySkillPoints);
            Assert.Equal(15, Tiers.SkillPointsToReach(15));
            Assert.Equal(15, Tiers.SkillPointsToReach(29));
            Assert.Equal("Biegłość I", Tiers.Get(15).LabelPl);
            Assert.Equal("skill_point_auto_promotion", Tiers.Get(15).RaisedBy);
        }

        [Theory]
        [InlineData(15, 1, true)]
        [InlineData(16, 19, false)]
        [InlineData(16, 20, true)]
        [InlineData(23, 59, false)]
        [InlineData(23, 60, true)]
        [InlineData(28, 89, false)]
        [InlineData(28, 90, true)]
        [InlineData(29, 94, false)]
        [InlineData(29, 95, true)]
        public void MinCharacterLevel(int rankIndex, int level, bool allowed)
        {
            Assert.Equal(allowed, Tiers.IsRankAllowedAtLevel(rankIndex, level));
        }
    }
}
