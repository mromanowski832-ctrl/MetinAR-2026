using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class MobLevelStatsTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Formula_MatchesTable(bool pr61)
        {
            var text = pr61 ? TestData.Pr61Table("mob_level_stats.csv") : TestData.RepoTable("mob_level_stats.csv");
            var rows = BalanceTableImporter.ReadMobLevelStats(text);
            if (pr61) Assert.Equal(99, rows.Count);
            foreach (var row in rows)
            {
                var s = MobLevelStats.ComputeNormal(Config, row.Level);
                Assert.True(row.Hp == s.Hp, "hp " + row.Level);
                Assert.True(row.Atk == s.Atk, "atk " + row.Level);
                Assert.True(row.Def == s.Def, "def " + row.Level);
                Assert.True(row.Exp == s.Exp, "exp " + row.Level);
                Assert.True(row.GoldAvg == s.GoldAvg, "goldAvg " + row.Level);
                Assert.True(row.GoldMin == s.GoldMin, "goldMin " + row.Level);
                Assert.True(row.GoldMax == s.GoldMax, "goldMax " + row.Level);
            }
        }

        [Fact]
        public void VenomousSpiderLevel5_KeepsPrototypeValues()
        {
            // MOBS_NPC.md §3: level 5, HP 120, Atk/Def 24/15, EXP 84, talary 10–18.
            var s = MobLevelStats.ComputeNormal(Config, 5);
            Assert.Equal(120, s.Hp);
            Assert.Equal(24, s.Atk);
            Assert.Equal(15, s.Def);
            Assert.Equal(84, s.Exp);
            Assert.Equal(10, s.GoldMin);
            Assert.Equal(18, s.GoldMax);
        }

        [Fact]
        public void RankMultipliers()
        {
            Assert.Equal(1, Config.Mobs.HpMultiplier(MobRank.Normal));
            Assert.Equal(3.5, Config.Mobs.HpMultiplier(MobRank.Elite));
            Assert.Equal(12, Config.Mobs.HpMultiplier(MobRank.Guardian));
            Assert.Equal(1.4, Config.Mobs.AtkMultiplier(MobRank.Elite));
            Assert.Equal(1.8, Config.Mobs.AtkMultiplier(MobRank.Guardian));
            Assert.Equal(3, Config.Mobs.RewardMultiplier(MobRank.Elite));
            Assert.Equal(10, Config.Mobs.RewardMultiplier(MobRank.Guardian));
        }

        [Fact]
        public void LevelOutOfRange_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MobLevelStats.ComputeNormal(Config, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MobLevelStats.ComputeNormal(Config, 100));
        }
    }
}
