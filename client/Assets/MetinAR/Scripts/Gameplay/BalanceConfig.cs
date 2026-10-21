using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay
{
    /// <summary>
    /// All balance numbers used by the pure gameplay core (METINAR-019 / S1). <see cref="CreateDefault"/> returns the
    /// values of the approved spec in docs/design (PR #44) with the owner's max level 99 (PR #61).
    /// Table-backed parts (rarity, temper, skill rank rules) can be replaced by imported tables, see
    /// <see cref="Data.BalanceTableImporter"/>.
    /// </summary>
    public sealed class BalanceConfig
    {
        /// <summary>Owner decision 2026-10-21: max character level 99 (spec proposal was 60).</summary>
        public int MaxCharacterLevel = 99;
        /// <summary>Max mob level (MOBS_NPC.md: mob level 1–99, clamped).</summary>
        public int MaxMobLevel = 99;
        /// <summary>Free stat points per level-up (CHARACTER_PROGRESSION.md §1.2).</summary>
        public int FreeStatPointsPerLevel = 2;

        public DerivedStatRules Stats = new DerivedStatRules();
        public DamageRules Damage = new DamageRules();
        public ExpRules Exp = new ExpRules();
        public MobRules Mobs = new MobRules();
        public SkillRankRules SkillRanks = new SkillRankRules();
        public List<RarityTier> Rarities = DefaultTables.Rarities();
        public List<TemperLevel> TemperLevels = DefaultTables.TemperLevels();

        private readonly Dictionary<ClassId, ClassBalance> _classes = new Dictionary<ClassId, ClassBalance>();

        public static BalanceConfig CreateDefault()
        {
            var config = new BalanceConfig();
            // CHARACTER_PROGRESSION.md §1.2: start Might/Finesse/Spirit/Vigor, auto +1 per level, HP/MP per level.
            config.SetClass(new ClassBalance(ClassId.Warrior, new PrimaryStats(6, 3, 2, 5), PrimaryStat.Might, PrimaryStat.Vigor, 14, 4));
            config.SetClass(new ClassBalance(ClassId.Ninja, new PrimaryStats(3, 6, 2, 4), PrimaryStat.Finesse, PrimaryStat.Vigor, 9, 5));
            config.SetClass(new ClassBalance(ClassId.Shaman, new PrimaryStats(2, 3, 6, 4), PrimaryStat.Spirit, PrimaryStat.Vigor, 8, 9));
            config.SetClass(new ClassBalance(ClassId.Slajter, new PrimaryStats(5, 2, 5, 4), PrimaryStat.Might, PrimaryStat.Spirit, 11, 7));
            config.Validate();
            return config;
        }

        public void SetClass(ClassBalance balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            _classes[balance.Id] = balance;
        }

        public ClassBalance GetClass(ClassId id)
        {
            ClassBalance balance;
            if (!_classes.TryGetValue(id, out balance)) throw new KeyNotFoundException("No balance for class " + id + ".");
            return balance;
        }

        public IEnumerable<ClassBalance> Classes { get { return _classes.Values; } }

        public RarityTier GetRarity(string rarityId)
        {
            foreach (var r in Rarities) if (r.Id == rarityId) return r;
            throw new KeyNotFoundException("Unknown rarity '" + rarityId + "'.");
        }

        /// <summary>Temper bonus to base stats as a fraction; temper 0 = no bonus.</summary>
        public double TemperBonus(int temperLevel)
        {
            if (temperLevel == 0) return 0;
            foreach (var t in TemperLevels) if (t.Level == temperLevel) return t.BaseStatBonus;
            throw new ArgumentOutOfRangeException(nameof(temperLevel), temperLevel, "Unknown temper level.");
        }

        /// <summary>Plain weapon attack of item level iL before rarity and temper: 4 + 1.6 × iL.</summary>
        public double WeaponAtk(int itemLevel)
        {
            if (itemLevel < 1) throw new ArgumentOutOfRangeException(nameof(itemLevel));
            return Stats.WeaponAtkBase + Stats.WeaponAtkPerItemLevel * itemLevel;
        }

        public void CheckCharacterLevel(int level)
        {
            if (level < 1 || level > MaxCharacterLevel)
                throw new ArgumentOutOfRangeException(nameof(level), level, "Character level must be 1.." + MaxCharacterLevel + ".");
        }

        /// <summary>Throws <see cref="InvalidOperationException"/> when the config is internally inconsistent.</summary>
        public void Validate()
        {
            var problems = new List<string>();
            if (MaxCharacterLevel < 2) problems.Add("MaxCharacterLevel < 2");
            if (MaxMobLevel < 1) problems.Add("MaxMobLevel < 1");
            foreach (ClassId id in Enum.GetValues(typeof(ClassId)))
                if (!_classes.ContainsKey(id)) problems.Add("missing class " + id);
            if (SkillRanks.BaseMaxRank < 1 || SkillRanks.MaxRank < SkillRanks.BaseMaxRank) problems.Add("invalid skill rank limits");
            int previous = 0;
            for (int rank = SkillRanks.BaseMaxRank + 1; rank <= SkillRanks.MaxRank; rank++)
            {
                int required;
                if (!SkillRanks.MasterRankMinCharacterLevel.TryGetValue(rank, out required)) { problems.Add("no level for master rank " + rank); continue; }
                if (required <= previous || required > MaxCharacterLevel) problems.Add("master rank " + rank + " level " + required + " not increasing within 1.." + MaxCharacterLevel);
                previous = required;
            }
            if (Rarities.Count == 0) problems.Add("no rarities");
            for (int i = 0; i < TemperLevels.Count; i++)
                if (TemperLevels[i].Level != i + 1) problems.Add("temper levels must be 1..n in order");
            if (Damage.HitChanceMin > Damage.HitChanceMax) problems.Add("hit chance bounds");
            if (Damage.VarianceMin > Damage.VarianceMax) problems.Add("variance bounds");
            if (problems.Count > 0) throw new InvalidOperationException("Invalid BalanceConfig: " + string.Join("; ", problems.ToArray()));
        }
    }
}
