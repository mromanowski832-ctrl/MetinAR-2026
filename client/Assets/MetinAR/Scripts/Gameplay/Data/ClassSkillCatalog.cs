using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay.Data
{
    /// <summary>One skill row of tables/classes_skills.json. Text fields are kept as in the table.</summary>
    public sealed class SkillDefinition
    {
        public string Id;
        public string NamePl;
        public ClassId Class;
        /// <summary>Path id, or null for the two class skills (levels 1 and 5).</summary>
        public string PathId;
        public int UnlockLevel;
        public string Kind;
        public string Target;
        /// <summary>atk, mag, hybrid, def, maxHp or none.</summary>
        public string Scaling;
        public double Coef;
        /// <summary>Mag coefficient of hybrid skills (0 when absent).</summary>
        public double MagCoef;
        public double MpCost;
        public double CooldownS;
        /// <summary>Effect duration in seconds (0 when absent).</summary>
        public double DurationS;
        /// <summary>Number of hits (0 when absent).</summary>
        public int Hits;
        /// <summary>HP cost in percent (0 when absent).</summary>
        public double HpCostPct;
        public string EffectPl;
        public string ArInput;
    }

    public sealed class SkillPathDefinition
    {
        public string Id;
        public string NamePl;
        public string RolePl;
        /// <summary>Required weapon/off-hand tag, or null.</summary>
        public string Requires;
        public List<SkillDefinition> Skills = new List<SkillDefinition>();
    }

    public sealed class ClassSkillsDefinition
    {
        public ClassId Class;
        public string NamePl;
        public string Role;
        public List<string> Weapons = new List<string>();
        public List<string> OffHand = new List<string>();
        public int PathChoiceLevel;
        public List<SkillDefinition> ClassSkills = new List<SkillDefinition>();
        public List<SkillPathDefinition> Paths = new List<SkillPathDefinition>();

        public IEnumerable<SkillDefinition> AllSkills()
        {
            foreach (var s in ClassSkills) yield return s;
            foreach (var p in Paths) foreach (var s in p.Skills) yield return s;
        }
    }

    /// <summary>Imported content of tables/classes_skills.json.</summary>
    public sealed class ClassSkillCatalog
    {
        // "rankRules" of classes_skills.json (tier system, CLASSES_SKILLS.md §2.1; ranks in skill_tiers.csv).
        public int MaxRankIndex;
        public int SkillPointsPerSkillToProficiency;
        public int SkillPointsPerLevelFromLevel2;
        public int MaxCharacterLevel;
        public int TotalSkillPointsAtMaxLevel;
        public List<ClassSkillsDefinition> Classes = new List<ClassSkillsDefinition>();

        public ClassSkillsDefinition GetClass(ClassId id)
        {
            foreach (var c in Classes) if (c.Class == id) return c;
            throw new KeyNotFoundException("Class " + id + " not in catalog.");
        }

        public IEnumerable<SkillDefinition> AllSkills()
        {
            foreach (var c in Classes) foreach (var s in c.AllSkills()) yield return s;
        }

        public SkillDefinition FindSkill(string skillId)
        {
            foreach (var s in AllSkills()) if (s.Id == skillId) return s;
            return null;
        }
    }
}
