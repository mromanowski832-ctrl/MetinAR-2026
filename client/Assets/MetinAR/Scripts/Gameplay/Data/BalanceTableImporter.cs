using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay.Data
{
    /// <summary>One row of tables/exp_curve.csv.</summary>
    public sealed class ExpCurveRow
    {
        public int Level;
        public long ExpToNext;
        public long CumulativeExpAtLevelStart;
        public double SameLevelKillEquivalents;
        public int SameLevelMobExp;
        public long ExpPerHudOrb;
    }

    /// <summary>
    /// Imports the spec tables from docs/design/tables (CSV/JSON text) into gameplay types. Text in, objects out:
    /// how the text is loaded (TextAsset, file, server) is up to the caller.
    /// </summary>
    public static class BalanceTableImporter
    {
        public static List<ExpCurveRow> ReadExpCurve(string csvText)
        {
            var t = CsvTable.Parse(csvText);
            var rows = new List<ExpCurveRow>();
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var row = new ExpCurveRow
                {
                    Level = t.GetInt(i, "level"),
                    ExpToNext = t.GetLong(i, "expToNext"),
                    CumulativeExpAtLevelStart = t.GetLong(i, "cumulativeExpAtLevelStart"),
                    SameLevelKillEquivalents = t.GetDouble(i, "sameLevelKillEquivalents"),
                    SameLevelMobExp = t.GetInt(i, "sameLevelMobExp"),
                    ExpPerHudOrb = t.GetLong(i, "expPerHudOrb"),
                };
                if (row.Level != i + 1) throw new FormatException("exp_curve.csv: expected level " + (i + 1) + ", got " + row.Level + ".");
                rows.Add(row);
            }
            if (rows.Count < 2) throw new FormatException("exp_curve.csv: needs at least 2 levels.");
            return rows;
        }

        /// <summary>ExpCurve with the table's max level (last row) and its expToNext values.</summary>
        public static ExpCurve ImportExpCurve(string csvText, ExpRules rules)
        {
            var rows = ReadExpCurve(csvText);
            int maxLevel = rows.Count;
            var next = new long[maxLevel + 1];
            foreach (var r in rows) next[r.Level] = r.ExpToNext;
            return ExpCurve.FromTable(rules, maxLevel, next);
        }

        public static List<MobLevelStats> ReadMobLevelStats(string csvText)
        {
            var t = CsvTable.Parse(csvText);
            var rows = new List<MobLevelStats>();
            for (int i = 0; i < t.Rows.Count; i++)
            {
                int level = t.GetInt(i, "mobLevel");
                if (level != i + 1) throw new FormatException("mob_level_stats.csv: expected level " + (i + 1) + ", got " + level + ".");
                rows.Add(new MobLevelStats(level, t.GetInt(i, "normalHp"), t.GetInt(i, "normalAtk"), t.GetInt(i, "normalDef"),
                    t.GetInt(i, "normalExp"), t.GetInt(i, "goldAvg"), t.GetInt(i, "goldMin"), t.GetInt(i, "goldMax")));
            }
            return rows;
        }

        public static List<RarityTier> ReadRarityTiers(string csvText)
        {
            var t = CsvTable.Parse(csvText);
            var rows = new List<RarityTier>();
            for (int i = 0; i < t.Rows.Count; i++)
                rows.Add(new RarityTier(t.Get(i, "rarityId"), t.GetInt(i, "affixCount"), t.GetDouble(i, "baseStatMultiplier")));
            return rows;
        }

        public static List<TemperLevel> ReadTemperLevels(string csvText)
        {
            var t = CsvTable.Parse(csvText);
            var rows = new List<TemperLevel>();
            for (int i = 0; i < t.Rows.Count; i++)
            {
                rows.Add(new TemperLevel(t.GetInt(i, "temperLevel"), t.GetDouble(i, "baseStatBonusPct") / 100.0,
                    t.GetDouble(i, "successChancePct") / 100.0, t.Get(i, "onFail"), t.GetInt(i, "oreShards")));
            }
            return rows;
        }

        public static ClassSkillCatalog ReadClassSkills(string jsonText)
        {
            var root = AsObject(MiniJson.Parse(jsonText), "root");
            var catalog = new ClassSkillCatalog();
            var rr = AsObject(Required(root, "rankRules"), "rankRules");
            catalog.MaxRankIndex = (int)Number(rr, "maxRankIndex");
            catalog.SkillPointsPerSkillToProficiency = (int)Number(rr, "skillPointsPerSkillToProficiency");
            catalog.SkillPointsPerLevelFromLevel2 = (int)Number(rr, "skillPointsPerLevelFromLevel2");
            catalog.MaxCharacterLevel = (int)Number(rr, "maxCharacterLevel");
            catalog.TotalSkillPointsAtMaxLevel = (int)Number(rr, "totalSkillPointsAtMaxLevel");

            foreach (var c in AsArray(Required(root, "classes"), "classes"))
            {
                var co = AsObject(c, "class");
                var cls = new ClassSkillsDefinition
                {
                    Class = ClassIds.Parse(Str(co, "id")),
                    NamePl = Str(co, "namePl"),
                    Role = OptStr(co, "role"),
                    PathChoiceLevel = (int)Number(co, "pathChoiceLevel"),
                };
                foreach (var w in AsArray(Required(co, "weapons"), "weapons")) cls.Weapons.Add((string)w);
                foreach (var w in AsArray(Required(co, "offHand"), "offHand")) cls.OffHand.Add((string)w);
                foreach (var s in AsArray(Required(co, "skills"), "skills")) cls.ClassSkills.Add(ReadSkill(AsObject(s, "skill"), cls.Class, null));
                foreach (var p in AsArray(Required(co, "paths"), "paths"))
                {
                    var po = AsObject(p, "path");
                    var path = new SkillPathDefinition { Id = Str(po, "id"), NamePl = Str(po, "namePl"), RolePl = OptStr(po, "rolePl"), Requires = OptStr(po, "requires") };
                    foreach (var s in AsArray(Required(po, "skills"), "skills")) path.Skills.Add(ReadSkill(AsObject(s, "skill"), cls.Class, path.Id));
                    cls.Paths.Add(path);
                }
                catalog.Classes.Add(cls);
            }
            return catalog;
        }

        /// <summary>tables/skill_tiers.csv → rank definitions (rankIndex 1…n). Percentages become fractions.</summary>
        public static List<SkillTierDefinition> ReadSkillTiers(string csvText)
        {
            var t = CsvTable.Parse(csvText);
            var rows = new List<SkillTierDefinition>();
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var d = new SkillTierDefinition
                {
                    RankIndex = t.GetInt(i, "rankIndex"),
                    TierId = t.Get(i, "tierId"),
                    TierRank = t.GetInt(i, "tierRank"),
                    LabelPl = t.Get(i, "labelPl"),
                    RaisedBy = t.Get(i, "raisedBy"),
                    SkillPointCost = t.GetInt(i, "skillPointCost"),
                    SuccessChance = t.GetDouble(i, "successChancePct") / 100.0,
                    AttemptCooldownHours = t.GetDouble(i, "attemptCooldownHours"),
                    ExpCostFractionOfExpToNext = t.GetDouble(i, "expCostPctOfExpToNext") / 100.0,
                    MinCharacterLevel = t.GetInt(i, "minCharacterLevel"),
                    CoefMult = t.GetDouble(i, "coefMult"),
                    MpCostMult = t.GetDouble(i, "mpCostMult"),
                    CooldownMult = t.GetDouble(i, "cooldownMult"),
                    DurationMult = t.GetDouble(i, "durationMult"),
                };
                if (d.RankIndex != i + 1) throw new FormatException("skill_tiers.csv: expected rankIndex " + (i + 1) + ", got " + d.RankIndex + ".");
                rows.Add(d);
            }
            return rows;
        }

        /// <summary>
        /// Replaces the table-backed parts of <paramref name="config"/> (rarity, temper, skill tiers) with imported
        /// tables and checks classes_skills.json "rankRules" against them. Pass null to keep a part. Validates the result.
        /// Note: the table values of skill_tiers.csv are rounded for display (coefMult to 0.01).
        /// </summary>
        public static void ApplyTables(BalanceConfig config, string rarityTiersCsv, string temperLevelsCsv, string skillTiersCsv, string classSkillsJson)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (rarityTiersCsv != null) config.Rarities = ReadRarityTiers(rarityTiersCsv);
            if (temperLevelsCsv != null) config.TemperLevels = ReadTemperLevels(temperLevelsCsv);
            if (skillTiersCsv != null) config.SkillTiers.Ranks = ReadSkillTiers(skillTiersCsv);
            if (classSkillsJson != null)
            {
                var catalog = ReadClassSkills(classSkillsJson);
                if (catalog.MaxCharacterLevel != config.MaxCharacterLevel)
                    throw new InvalidOperationException("classes_skills.json maxCharacterLevel " + catalog.MaxCharacterLevel + " != config " + config.MaxCharacterLevel + ".");
                if (catalog.MaxRankIndex != config.SkillTiers.MaxRankIndex)
                    throw new InvalidOperationException("classes_skills.json maxRankIndex " + catalog.MaxRankIndex + " != skill tiers " + config.SkillTiers.MaxRankIndex + ".");
                if (catalog.SkillPointsPerSkillToProficiency != config.SkillTiers.MaxRankBySkillPoints)
                    throw new InvalidOperationException("classes_skills.json skillPointsPerSkillToProficiency " + catalog.SkillPointsPerSkillToProficiency + " != skill tiers " + config.SkillTiers.MaxRankBySkillPoints + ".");
                config.SkillTiers.SkillPointsPerLevelFromLevel2 = catalog.SkillPointsPerLevelFromLevel2;
            }
            config.Validate();
        }

        private static SkillDefinition ReadSkill(Dictionary<string, object> o, ClassId cls, string pathId)
        {
            return new SkillDefinition
            {
                Id = Str(o, "id"),
                NamePl = Str(o, "namePl"),
                Class = cls,
                PathId = pathId,
                UnlockLevel = (int)Number(o, "unlockLevel"),
                Kind = Str(o, "kind"),
                Target = Str(o, "target"),
                Scaling = Str(o, "scaling"),
                Coef = Number(o, "coef"),
                MagCoef = OptNumber(o, "magCoef"),
                MpCost = Number(o, "mpCost"),
                CooldownS = Number(o, "cooldownS"),
                DurationS = OptNumber(o, "durationS"),
                Hits = (int)OptNumber(o, "hits"),
                HpCostPct = OptNumber(o, "hpCostPct"),
                EffectPl = OptStr(o, "effectPl"),
                ArInput = OptStr(o, "arInput"),
            };
        }

        private static object Required(Dictionary<string, object> o, string key)
        {
            object v;
            if (!o.TryGetValue(key, out v)) throw new FormatException("Missing JSON field '" + key + "'.");
            return v;
        }

        private static Dictionary<string, object> AsObject(object v, string what)
        {
            var d = v as Dictionary<string, object>;
            if (d == null) throw new FormatException("JSON '" + what + "' is not an object.");
            return d;
        }

        private static List<object> AsArray(object v, string what)
        {
            var l = v as List<object>;
            if (l == null) throw new FormatException("JSON '" + what + "' is not an array.");
            return l;
        }

        private static double AsNumber(object v, string what)
        {
            if (!(v is double)) throw new FormatException("JSON '" + what + "' is not a number.");
            return (double)v;
        }

        private static double Number(Dictionary<string, object> o, string key) { return AsNumber(Required(o, key), key); }

        private static double OptNumber(Dictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? AsNumber(v, key) : 0;
        }

        private static string Str(Dictionary<string, object> o, string key)
        {
            var s = Required(o, key) as string;
            if (s == null) throw new FormatException("JSON '" + key + "' is not a string.");
            return s;
        }

        private static string OptStr(Dictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) ? v as string : null;
        }
    }
}
