using System;

namespace MetinAR.Gameplay
{
    /// <summary>Derived character stats (CHARACTER_PROGRESSION.md §1.3). Unrounded: the spec rounds only final damage.</summary>
    public sealed class DerivedStats
    {
        public int Level;
        public PrimaryStats Primary;
        public double MaxHp;
        public double MaxMp;
        public double Atk;
        public double Mag;
        public double Def;
        public double Accuracy;
        public double Evasion;
        public double CritChance;
        public double CritMultiplier;
        public double BlockChance;
        public double HpRegenPer5sOutOfCombat;
        public double HpRegenPer5sInCombat;
        public double MpRegenPerSecondInCombat;
        public double MpRegenPerSecondOutOfCombat;
        public double ResistPoison;
        public double ResistDark;
        public double ResistElemental;

        public double Resistance(DamageType type)
        {
            switch (type)
            {
                case DamageType.Physical: return 0;
                case DamageType.Elemental: return ResistElemental;
                case DamageType.Dark: return ResistDark;
                case DamageType.Poison: return ResistPoison;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }

    public static class DerivedStatsCalculator
    {
        /// <summary>Free stat points available at a level: 2 × (L − 1).</summary>
        public static int FreePointsAtLevel(BalanceConfig config, int level)
        {
            config.CheckCharacterLevel(level);
            return config.FreeStatPointsPerLevel * (level - 1);
        }

        /// <summary>Primary stats = class start + auto growth (+1 in two class stats per level-up) + allocated free points.</summary>
        public static PrimaryStats PrimaryStatsFor(BalanceConfig config, CharacterBuild build)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (build == null) throw new ArgumentNullException(nameof(build));
            config.CheckCharacterLevel(build.Level);
            PrimaryStats a = build.AllocatedPoints;
            if (a.Might < 0 || a.Finesse < 0 || a.Spirit < 0 || a.Vigor < 0) throw new ArgumentException("Allocated points cannot be negative.");
            int free = FreePointsAtLevel(config, build.Level);
            if (a.Total > free) throw new ArgumentException("Allocated " + a.Total + " points but only " + free + " are available at level " + build.Level + ".");
            return config.GetClass(build.Class).AutoStatsAtLevel(build.Level) + a;
        }

        public static DerivedStats Compute(BalanceConfig config, CharacterBuild build, WeaponLoadout weapon, EquipmentBonuses items)
        {
            if (weapon == null) weapon = WeaponLoadout.Unarmed;
            if (items == null) items = EquipmentBonuses.None;
            PrimaryStats p = PrimaryStatsFor(config, build);
            ClassBalance cls = config.GetClass(build.Class);
            DerivedStatRules r = config.Stats;
            int level = build.Level;
            double temper = config.TemperBonus(weapon.TemperLevel);

            var s = new DerivedStats { Level = level, Primary = p };
            s.MaxHp = r.BaseHp + level * cls.HpPerLevel + p.Vigor * r.HpPerVigor + items.MaxHp;
            s.MaxMp = r.BaseMp + level * cls.MpPerLevel + p.Spirit * r.MpPerSpirit + items.MaxMp;
            s.Atk = weapon.BaseAtk * (1 + temper) + p.Get(weapon.ScalingStat) * r.AtkPerScalingStat + level * r.AtkPerLevel;
            s.Mag = weapon.BaseMag * (1 + temper) + p.Spirit * r.MagPerSpirit + level * r.MagPerLevel;
            s.Def = items.Defense + p.Vigor * r.DefPerVigor;
            s.Accuracy = r.BaseAccuracy + p.Finesse * r.AccuracyPerFinesse + level * r.AccuracyPerLevel + items.Accuracy;
            s.Evasion = r.BaseEvasion + p.Finesse * r.EvasionPerFinesse + items.Evasion;
            s.CritChance = Math.Min(r.CritChanceCap, r.BaseCritChance + p.Finesse * r.CritChancePerFinesse + items.CritChance);
            s.CritMultiplier = Math.Min(r.CritMultiplierCap, r.BaseCritMultiplier + items.CritMultiplier);
            s.BlockChance = items.HasShield ? GameplayMath.Clamp(items.BlockChance, 0, r.BlockChanceCap) : 0;
            s.HpRegenPer5sOutOfCombat = s.MaxHp * r.HpRegenOutOfCombatFractionPer5s;
            s.HpRegenPer5sInCombat = items.HpRegenPer5s;
            s.MpRegenPerSecondInCombat = r.MpRegenBasePerSecond + p.Spirit * r.MpRegenPerSpiritPerSecond;
            s.MpRegenPerSecondOutOfCombat = s.MpRegenPerSecondInCombat * r.MpRegenOutOfCombatMultiplier;
            s.ResistPoison = GameplayMath.Clamp(items.ResistPoison, 0, r.ResistanceCap);
            s.ResistDark = GameplayMath.Clamp(items.ResistDark, 0, r.ResistanceCap);
            s.ResistElemental = GameplayMath.Clamp(items.ResistElemental, 0, r.ResistanceCap);
            return s;
        }

        /// <summary>Attack speed modifier clamped to 0.80–1.25× (§1.4).</summary>
        public static double ClampAttackSpeedModifier(BalanceConfig config, double modifier)
        {
            return GameplayMath.Clamp(modifier, config.Stats.AttackSpeedModifierMin, config.Stats.AttackSpeedModifierMax);
        }
    }
}
