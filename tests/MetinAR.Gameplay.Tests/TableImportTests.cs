using System;
using System.Collections.Generic;
using System.Linq;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class TableImportTests
    {
        [Fact]
        public void DefaultConfig_IsValid_WithFourOwnerClasses()
        {
            var config = BalanceConfig.CreateDefault();
            config.Validate();
            Assert.Equal(4, config.Classes.Count());
            Assert.Equal(14, config.GetClass(ClassId.Warrior).HpPerLevel);
            Assert.Equal(7, config.GetClass(ClassId.Slajter).MpPerLevel);
            Assert.Equal("slajter", ClassIds.ToDataId(ClassId.Slajter));
            Assert.Equal(ClassId.Shaman, ClassIds.Parse("shaman"));
            Assert.Throws<FormatException>(() => ClassIds.Parse("sura"));
        }

        [Fact]
        public void ClassSkills_Import_56Skills()
        {
            var catalog = BalanceTableImporter.ReadClassSkills(TestData.RepoTable("classes_skills.json"));
            Assert.Equal(4, catalog.Classes.Count);
            var all = catalog.AllSkills().ToList();
            Assert.Equal(56, all.Count);
            Assert.Equal(56, all.Select(s => s.Id).Distinct().Count());
            foreach (ClassId cls in Enum.GetValues(typeof(ClassId)))
            {
                var c = catalog.GetClass(cls);
                Assert.Equal(10, c.PathChoiceLevel);
                Assert.Equal(new[] { 1, 5 }, c.ClassSkills.Select(s => s.UnlockLevel).ToArray());
                Assert.Equal(2, c.Paths.Count);
                foreach (var p in c.Paths)
                    Assert.Equal(new[] { 10, 12, 15, 20, 26, 34 }, p.Skills.Select(s => s.UnlockLevel).ToArray());
                Assert.All(c.AllSkills(), s => Assert.Equal(cls, s.Class));
            }
            var gloom = catalog.FindSkill("slajter_gloom_thrust");
            Assert.Equal("hybrid", gloom.Scaling);
            Assert.Equal(1.2, gloom.Coef, 9);
            Assert.Equal(0.4, gloom.MagCoef, 9);
            Assert.Null(gloom.PathId);
            Assert.Equal("bulwark", catalog.FindSkill("bulwark_riposte").PathId);
        }

        [Fact]
        public void RankRules_MatchSkillTierConfig()
        {
            var catalog = BalanceTableImporter.ReadClassSkills(TestData.RepoTable("classes_skills.json"));
            var tiers = BalanceConfig.CreateDefault().SkillTiers;
            Assert.Equal(99, catalog.MaxCharacterLevel);
            Assert.Equal(tiers.MaxRankIndex, catalog.MaxRankIndex);
            Assert.Equal(tiers.MaxRankBySkillPoints, catalog.SkillPointsPerSkillToProficiency);
            Assert.Equal(tiers.SkillPointsPerLevelFromLevel2, catalog.SkillPointsPerLevelFromLevel2);
            Assert.Equal(tiers.SkillPointsAtLevel(99), catalog.TotalSkillPointsAtMaxLevel);
        }

        [Fact]
        public void RarityAndTemperTables_EqualDefaultConfig()
        {
            var config = BalanceConfig.CreateDefault();
            var rarities = BalanceTableImporter.ReadRarityTiers(TestData.RepoTable("rarity_tiers.csv"));
            Assert.Equal(config.Rarities.Select(r => r.Id), rarities.Select(r => r.Id));
            for (int i = 0; i < rarities.Count; i++)
            {
                Assert.Equal(config.Rarities[i].AffixCount, rarities[i].AffixCount);
                Assert.Equal(config.Rarities[i].BaseStatMultiplier, rarities[i].BaseStatMultiplier, 12);
            }

            var temper = BalanceTableImporter.ReadTemperLevels(TestData.RepoTable("temper_levels.csv"));
            Assert.Equal(10, temper.Count);
            for (int i = 0; i < temper.Count; i++)
            {
                Assert.Equal(config.TemperLevels[i].Level, temper[i].Level);
                Assert.Equal(config.TemperLevels[i].BaseStatBonus, temper[i].BaseStatBonus, 12);
                Assert.Equal(config.TemperLevels[i].SuccessChance, temper[i].SuccessChance, 12);
                Assert.Equal(config.TemperLevels[i].OnFail, temper[i].OnFail);
                Assert.Equal(config.TemperLevels[i].OreShards, temper[i].OreShards);
            }
            Assert.Equal(0.60, config.TemperBonus(10), 12);
            Assert.Equal(0, config.TemperBonus(0), 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.TemperBonus(11));
        }

        [Fact]
        public void ApplyTables_WithRepoTables_Validates()
        {
            var config = BalanceConfig.CreateDefault();
            BalanceTableImporter.ApplyTables(config, TestData.RepoTable("rarity_tiers.csv"), TestData.RepoTable("temper_levels.csv"),
                TestData.RepoTable("skill_tiers.csv"), TestData.RepoTable("classes_skills.json"));
            Assert.Equal(29, config.SkillTiers.MaxRankIndex);
            Assert.Equal(3.3, config.SkillTiers.CoefMultiplier(29), 9);
            Assert.Equal(60, config.SkillTiers.Get(23).MinCharacterLevel);
        }

        [Fact]
        public void ApplyTables_RejectsMismatchedMaxLevel()
        {
            var config = BalanceConfig.CreateDefault();
            config.MaxCharacterLevel = 60;
            Assert.Throws<InvalidOperationException>(() =>
                BalanceTableImporter.ApplyTables(config, null, null, null, TestData.RepoTable("classes_skills.json")));
        }

        [Fact]
        public void Validate_RejectsBrokenSkillTiers()
        {
            var config = BalanceConfig.CreateDefault();
            config.SkillTiers.Ranks[5].RankIndex = 99;
            Assert.Throws<InvalidOperationException>(() => config.Validate());

            config = BalanceConfig.CreateDefault();
            config.SkillTiers.Ranks[28].MinCharacterLevel = 120;
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Fact]
        public void Csv_QuotesBlankLinesAndErrors()
        {
            var t = CsvTable.Parse("\uFEFFa,b\r\n1,\"x, \"\"y\"\"\"\r\n\r\n2,z\n");
            Assert.Equal(2, t.Rows.Count);
            Assert.Equal("x, \"y\"", t.Get(0, "b"));
            Assert.Equal(2, t.GetInt(1, "a"));
            Assert.Throws<FormatException>(() => t.Column("c"));
            Assert.Throws<FormatException>(() => t.GetInt(1, "b"));
            Assert.Throws<FormatException>(() => CsvTable.Parse("a,b\n1\n"));
            Assert.Throws<FormatException>(() => CsvTable.Parse("a\n\"open\n"));
        }

        [Fact]
        public void MiniJson_ParsesAndRejects()
        {
            var o = (Dictionary<string, object>)MiniJson.Parse("{\"a\": [1, -2.5e1, true, false, null, \"\\u0141\\n\"], \"b\": {}}");
            var a = (List<object>)o["a"];
            Assert.Equal(1.0, a[0]);
            Assert.Equal(-25.0, a[1]);
            Assert.Equal(true, a[2]);
            Assert.Equal(false, a[3]);
            Assert.Null(a[4]);
            Assert.Equal("Ł\n", a[5]);
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1,}"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1} x"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1,\"a\":2}"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("[1"));
        }
    }
}
