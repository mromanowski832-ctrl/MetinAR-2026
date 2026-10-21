using System;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    /// <summary>
    /// Reproduces tables/sanity_warrior_ttk.csv with production code: a warrior who puts 2/3 of free points into Might
    /// (rest into Vigor), plain weapon of iL = level (common below 10, rare below 30, legendary from 30, as in
    /// generate_tables.py), versus a normal mob of the same level; plus "Ciężkie Cięcie" (coef from
    /// classes_skills.json) at the rank named in the table. The table has 1 decimal (3 for mitigation, 2 for coefMult).
    /// </summary>
    public class SanityWarriorTtkTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly DamageResolver Resolver = new DamageResolver(Config);

        [Fact]
        public void ProductionFormulas_ReproduceTable()
        {
            var t = CsvTable.Parse(TestData.RepoTable("sanity_warrior_ttk.csv"));
            Assert.Equal(12, t.Rows.Count); // 1, 5, 10, 20 … 90, 99
            var heavySlash = BalanceTableImporter.ReadClassSkills(TestData.RepoTable("classes_skills.json")).FindSkill("warrior_heavy_slash");
            Assert.NotNull(heavySlash);

            for (int i = 0; i < t.Rows.Count; i++)
            {
                int level = t.GetInt(i, "level");
                int free = DerivedStatsCalculator.FreePointsAtLevel(Config, level);
                int toMight = GameplayMath.RoundToInt(free * 2.0 / 3.0);
                var build = new CharacterBuild(ClassId.Warrior, level, new PrimaryStats(toMight, 0, 0, free - toMight));
                string rarity = level < 10 ? "common" : (level < 30 ? "rare" : "legendary");
                var weapon = WeaponLoadout.PlainWeapon(Config, level, rarity, 0, PrimaryStat.Might);
                var stats = DerivedStatsCalculator.Compute(Config, build, weapon, null);
                var mob = MobLevelStats.ComputeNormal(Config, level);
                var request = new DamageRequest { AttackerLevel = level, TargetLevel = level, Power = stats.Atk, TargetDefense = mob.Def };
                double mitigation = Resolver.Mitigation(mob.Def, level, DamageKind.Physical);
                double avgHit = Resolver.ExpectedHitNoCrit(request);
                string at = " (level " + level + ")";

                Assert.True(t.GetInt(i, "might") == stats.Primary.Might, "might" + at);
                Assert.True(t.GetInt(i, "vigor") == stats.Primary.Vigor, "vigor" + at);
                AssertClose(t.GetDouble(i, "atk"), stats.Atk, 0.05, "atk" + at);
                AssertClose(t.GetDouble(i, "mobMitigation"), mitigation, 0.0005, "mitigation" + at);
                AssertClose(t.GetDouble(i, "avgHitNoCrit"), avgHit, 0.05, "avgHit" + at);
                Assert.True(t.GetInt(i, "mobHp") == mob.Hp, "mobHp" + at);
                AssertClose(t.GetDouble(i, "hitsToKill"), mob.Hp / avgHit, 0.05, "hitsToKill" + at);
                AssertClose(t.GetDouble(i, "playerMaxHp"), stats.MaxHp, 1e-9, "playerMaxHp" + at);

                // Heavy Slash at the assumed rank: same pipeline with coef × coefMult(rank).
                int rank = RankIndexForLabel(t.Get(i, "assumedHeavySlashRank"));
                // Note: the table assumes Kunszt III at level 70 and Kunszt V at level 80, below their
                // minCharacterLevel (72 / 84). Reported in #41; here only the damage formulas are checked.
                double coefMult = Config.SkillTiers.CoefMultiplier(rank);
                AssertClose(t.GetDouble(i, "heavySlashCoefMult"), coefMult, 0.005, "heavySlashCoefMult" + at);
                var skillRequest = new DamageRequest
                {
                    AttackerLevel = level, TargetLevel = level, Power = stats.Atk, TargetDefense = mob.Def,
                    Coefficient = heavySlash.Coef * coefMult,
                };
                double skillHit = Resolver.ExpectedHitNoCrit(skillRequest);
                AssertClose(t.GetDouble(i, "heavySlashHit"), skillHit, 0.05, "heavySlashHit" + at);
                AssertClose(t.GetDouble(i, "heavySlashOnlyHitsToKill"), mob.Hp / skillHit, 0.05, "heavySlashOnlyHitsToKill" + at);

                // Full resolver with neutral rolls (hit, spread ×1.0, no crit) gives the rounded average hit.
                var rng = new ScriptedRandom(0.0, 0.5, 0.999999);
                request.AttackerAccuracy = stats.Accuracy;
                request.CritChance = stats.CritChance;
                var result = Resolver.Resolve(request, rng);
                Assert.Equal(HitOutcome.Hit, result.Outcome);
                Assert.False(result.IsCritical);
                Assert.Equal(GameplayMath.RoundToInt(avgHit), result.Damage);
            }
        }

        private static int RankIndexForLabel(string label)
        {
            foreach (var r in Config.SkillTiers.Ranks) if (r.LabelPl == label) return r.RankIndex;
            throw new InvalidOperationException("Unknown rank label '" + label + "'.");
        }

        private static void AssertClose(double expected, double actual, double tolerance, string what)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance + 1e-9, what + ": expected " + expected + ", actual " + actual);
        }
    }
}
