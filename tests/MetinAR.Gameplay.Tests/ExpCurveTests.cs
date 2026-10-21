using System;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class ExpCurveTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly ExpCurve Curve = new ExpCurve(Config);

        [Fact]
        public void MaxLevelIs99_OwnerDecision()
        {
            Assert.Equal(99, Config.MaxCharacterLevel);
            Assert.Equal(99, Curve.MaxLevel);
            Assert.Equal(0, Curve.ExpToNext(99));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curve.ExpToNext(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curve.ExpToNext(100));
        }

        [Fact]
        public void Formula_MatchesPr61Table_AllLevels1To99()
        {
            var rows = BalanceTableImporter.ReadExpCurve(TestData.Pr61Table("exp_curve.csv"));
            Assert.Equal(99, rows.Count);
            foreach (var row in rows)
            {
                Assert.True(row.ExpToNext == Curve.ExpToNext(row.Level), "expToNext level " + row.Level);
                Assert.True(row.CumulativeExpAtLevelStart == Curve.CumulativeExpAtLevelStart(row.Level), "cumulative level " + row.Level);
                Assert.True(row.SameLevelMobExp == Curve.MobExp(row.Level), "mobExp level " + row.Level);
                Assert.True(row.ExpPerHudOrb == Curve.ExpPerHudOrb(row.Level), "orb level " + row.Level);
                double expectedK = row.Level < 99 ? Curve.KillEquivalents(row.Level) : 0;
                Assert.True(Math.Abs(row.SameLevelKillEquivalents - expectedK) < 0.05 + 1e-9, "K level " + row.Level);
            }
        }

        [Fact]
        public void Formula_MatchesRepoTable_BelowItsMaxLevel()
        {
            // docs/design/tables/exp_curve.csv on the current base (1–60 from PR #44, or 1–99 after PR #61).
            var rows = BalanceTableImporter.ReadExpCurve(TestData.RepoTable("exp_curve.csv"));
            int tableMax = rows.Count;
            Assert.True(tableMax == 60 || tableMax == 99, "unexpected repo table max level " + tableMax);
            foreach (var row in rows)
            {
                Assert.True(row.CumulativeExpAtLevelStart == Curve.CumulativeExpAtLevelStart(row.Level), "cumulative level " + row.Level);
                Assert.True(row.SameLevelMobExp == Curve.MobExp(row.Level), "mobExp level " + row.Level);
                if (row.Level < tableMax)
                {
                    Assert.True(row.ExpToNext == Curve.ExpToNext(row.Level), "expToNext level " + row.Level);
                    Assert.True(row.ExpPerHudOrb == Curve.ExpPerHudOrb(row.Level), "orb level " + row.Level);
                }
            }
        }

        [Fact]
        public void Pr61Fixture_MatchesRepoTableOnOverlap()
        {
            var repo = BalanceTableImporter.ReadExpCurve(TestData.RepoTable("exp_curve.csv"));
            var pr61 = BalanceTableImporter.ReadExpCurve(TestData.Pr61Table("exp_curve.csv"));
            for (int i = 0; i < repo.Count - 1; i++)
            {
                Assert.Equal(repo[i].ExpToNext, pr61[i].ExpToNext);
                Assert.Equal(repo[i].CumulativeExpAtLevelStart, pr61[i].CumulativeExpAtLevelStart);
            }
            Assert.Equal(repo[repo.Count - 1].CumulativeExpAtLevelStart, pr61[repo.Count - 1].CumulativeExpAtLevelStart);
        }

        [Theory]
        // CHARACTER_PROGRESSION.md §2 table (PR #61 version).
        [InlineData(1, 160, 0)]
        [InlineData(5, 840, 1460)]
        [InlineData(10, 2580, 8720)]
        [InlineData(20, 9320, 61230)]
        [InlineData(30, 20800, 202180)]
        [InlineData(40, 37230, 479840)]
        [InlineData(59, 82880, 1567050)]
        [InlineData(60, 85800, 1649930)]
        [InlineData(80, 156400, 3998640)]
        [InlineData(98, 239430, 7491640)]
        [InlineData(99, 0, 7731070)]
        public void SpecAnchors(int level, long expToNext, long cumulative)
        {
            Assert.Equal(expToNext, Curve.ExpToNext(level));
            Assert.Equal(cumulative, Curve.CumulativeExpAtLevelStart(level));
        }

        [Fact]
        public void ImportedCurve_EqualsFormulaCurve()
        {
            var imported = BalanceTableImporter.ImportExpCurve(TestData.Pr61Table("exp_curve.csv"), Config.Exp);
            Assert.Equal(99, imported.MaxLevel);
            for (int level = 1; level <= 99; level++)
            {
                Assert.Equal(Curve.ExpToNext(level), imported.ExpToNext(level));
                Assert.Equal(Curve.CumulativeExpAtLevelStart(level), imported.CumulativeExpAtLevelStart(level));
            }
            Assert.Equal(7731070, imported.TotalExpToMaxLevel);
        }

        [Fact]
        public void LevelForTotalExp_Boundaries()
        {
            Assert.Equal(1, Curve.LevelForTotalExp(0));
            Assert.Equal(1, Curve.LevelForTotalExp(159));
            Assert.Equal(2, Curve.LevelForTotalExp(160));
            Assert.Equal(5, Curve.LevelForTotalExp(1460));
            Assert.Equal(98, Curve.LevelForTotalExp(7731069));
            Assert.Equal(99, Curve.LevelForTotalExp(7731070));
            Assert.Equal(99, Curve.LevelForTotalExp(long.MaxValue / 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curve.LevelForTotalExp(-1));
            for (int level = 1; level <= 99; level++)
                Assert.Equal(level, Curve.LevelForTotalExp(Curve.CumulativeExpAtLevelStart(level)));
        }

        [Fact]
        public void HudOrbs_FractionalFill()
        {
            // Level 1 needs 160: 40 EXP per orb.
            Assert.Equal(40, Curve.ExpPerHudOrb(1));
            Assert.Equal(0.0, Curve.FilledHudOrbs(0), 9);
            Assert.Equal(1.0, Curve.FilledHudOrbs(40), 9);
            Assert.Equal(2.5, Curve.FilledHudOrbs(100), 9);
            Assert.Equal(0.0, Curve.FilledHudOrbs(160), 9); // level 2 just started
            Assert.Equal(4.0, Curve.FilledHudOrbs(Curve.TotalExpToMaxLevel), 9);
            Assert.Equal(60, Curve.ExpIntoLevel(220));
        }

        [Theory]
        // §2.2: (mob level − player level) → multiplier
        [InlineData(15, 10, 1.25)]
        [InlineData(20, 10, 1.25)]
        [InlineData(14, 10, 1.20)]
        [InlineData(11, 10, 1.05)]
        [InlineData(10, 10, 1.00)]
        [InlineData(5, 10, 1.00)]
        [InlineData(4, 10, 0.85)]
        [InlineData(1, 10, 0.40)]
        [InlineData(1, 11, 0.25)]
        [InlineData(1, 12, 0.10)]
        [InlineData(1, 40, 0.10)]
        public void LevelDifferenceMultiplier(int mobLevel, int playerLevel, double expected)
        {
            Assert.Equal(expected, Curve.LevelDifferenceMultiplier(mobLevel, playerLevel), 9);
        }
    }
}
