using System;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class DerivedStatsTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();

        [Theory]
        // CHARACTER_PROGRESSION.md §1.2 starting stats.
        [InlineData(ClassId.Warrior, 6, 3, 2, 5)]
        [InlineData(ClassId.Ninja, 3, 6, 2, 4)]
        [InlineData(ClassId.Shaman, 2, 3, 6, 4)]
        [InlineData(ClassId.Slajter, 5, 2, 5, 4)]
        public void StartingStats(ClassId cls, int might, int finesse, int spirit, int vigor)
        {
            var p = DerivedStatsCalculator.PrimaryStatsFor(Config, new CharacterBuild(cls, 1, default(PrimaryStats)));
            Assert.Equal(new PrimaryStats(might, finesse, spirit, vigor), p);
        }

        [Theory]
        // Auto +1 in two class stats per level-up; level 10 = 9 level-ups.
        [InlineData(ClassId.Warrior, 15, 3, 2, 14)]
        [InlineData(ClassId.Ninja, 3, 15, 2, 13)]
        [InlineData(ClassId.Shaman, 2, 3, 15, 13)]
        [InlineData(ClassId.Slajter, 14, 2, 14, 4)]
        public void AutoGrowthAtLevel10(ClassId cls, int might, int finesse, int spirit, int vigor)
        {
            var p = DerivedStatsCalculator.PrimaryStatsFor(Config, new CharacterBuild(cls, 10, default(PrimaryStats)));
            Assert.Equal(new PrimaryStats(might, finesse, spirit, vigor), p);
        }

        [Fact]
        public void FreePoints_TwoPerLevelUp_AndOverAllocationThrows()
        {
            Assert.Equal(0, DerivedStatsCalculator.FreePointsAtLevel(Config, 1));
            Assert.Equal(196, DerivedStatsCalculator.FreePointsAtLevel(Config, 99));
            var ok = new CharacterBuild(ClassId.Warrior, 3, new PrimaryStats(4, 0, 0, 0));
            Assert.Equal(12, DerivedStatsCalculator.PrimaryStatsFor(Config, ok).Might);
            Assert.Throws<ArgumentException>(() => DerivedStatsCalculator.PrimaryStatsFor(Config, new CharacterBuild(ClassId.Warrior, 3, new PrimaryStats(5, 0, 0, 0))));
            Assert.Throws<ArgumentException>(() => DerivedStatsCalculator.PrimaryStatsFor(Config, new CharacterBuild(ClassId.Warrior, 3, new PrimaryStats(-1, 0, 0, 0))));
            Assert.Throws<ArgumentOutOfRangeException>(() => DerivedStatsCalculator.PrimaryStatsFor(Config, new CharacterBuild(ClassId.Warrior, 100, default(PrimaryStats))));
        }

        [Fact]
        public void Level1Warrior_WithStarterSword_HandComputed()
        {
            var build = new CharacterBuild(ClassId.Warrior, 1, default(PrimaryStats));
            var sword = new WeaponLoadout { BaseAtk = 6, ScalingStat = PrimaryStat.Might }; // starter_sword Atk 6
            var s = DerivedStatsCalculator.Compute(Config, build, sword, null);
            Assert.Equal(60 + 14 + 5 * 6, s.MaxHp, 9);          // 104
            Assert.Equal(20 + 4 + 2 * 5, s.MaxMp, 9);           // 34
            Assert.Equal(6 + 6 * 0.8 + 0.5, s.Atk, 9);          // 11.3
            Assert.Equal(2 + 0.5, s.Mag, 9);
            Assert.Equal(5 * 0.8, s.Def, 9);
            Assert.Equal(70 + 3 + 1, s.Accuracy, 9);
            Assert.Equal(5 + 1.5, s.Evasion, 9);
            Assert.Equal(0.03 + 3 * 0.0015, s.CritChance, 9);
            Assert.Equal(1.6, s.CritMultiplier, 9);
            Assert.Equal(0, s.BlockChance, 9);
            Assert.Equal(104 * 0.02, s.HpRegenPer5sOutOfCombat, 9);
            Assert.Equal(0, s.HpRegenPer5sInCombat, 9);
            Assert.Equal(1 + 2 * 0.05, s.MpRegenPerSecondInCombat, 9);
            Assert.Equal(3 * (1 + 2 * 0.05), s.MpRegenPerSecondOutOfCombat, 9);
        }

        [Fact]
        public void Level1Shaman_HandComputed()
        {
            var s = DerivedStatsCalculator.Compute(Config, new CharacterBuild(ClassId.Shaman, 1, default(PrimaryStats)), null, null);
            Assert.Equal(60 + 8 + 4 * 6, s.MaxHp, 9);
            Assert.Equal(20 + 9 + 6 * 5, s.MaxMp, 9);
            Assert.Equal(6 + 0.5, s.Mag, 9);
        }

        [Fact]
        public void StarterEquipment_AddsItemBonuses()
        {
            // items_starter.csv: jerkin def 4 / +10 HP, boots def 2 / evasion 2, gloves def 1 / accuracy 2,
            // buckler def 3 / block 8%, amber earring poison res 5%.
            var items = new EquipmentBonuses
            {
                Defense = 4 + 2 + 1 + 3, MaxHp = 10, Evasion = 2, Accuracy = 2,
                HasShield = true, BlockChance = 0.08, ResistPoison = 0.05,
            };
            var s = DerivedStatsCalculator.Compute(Config, new CharacterBuild(ClassId.Warrior, 1, default(PrimaryStats)), null, items);
            Assert.Equal(114, s.MaxHp, 9);
            Assert.Equal(10 + 4, s.Def, 9);
            Assert.Equal(8.5, s.Evasion, 9);
            Assert.Equal(76, s.Accuracy, 9);
            Assert.Equal(0.08, s.BlockChance, 9);
            Assert.Equal(0.05, s.Resistance(DamageType.Poison), 9);
            Assert.Equal(0, s.Resistance(DamageType.Physical), 9);
        }

        [Fact]
        public void Caps_Crit40_CritMult22_Block35_Res50_BlockNeedsShield()
        {
            var items = new EquipmentBonuses
            {
                CritChance = 0.9, CritMultiplier = 5, BlockChance = 0.9, HasShield = true,
                ResistPoison = 0.9, ResistDark = 0.7, ResistElemental = 0.2,
            };
            var s = DerivedStatsCalculator.Compute(Config, new CharacterBuild(ClassId.Ninja, 99, default(PrimaryStats)), null, items);
            Assert.Equal(0.40, s.CritChance, 9);
            Assert.Equal(2.2, s.CritMultiplier, 9);
            Assert.Equal(0.35, s.BlockChance, 9);
            Assert.Equal(0.5, s.ResistPoison, 9);
            Assert.Equal(0.5, s.ResistDark, 9);
            Assert.Equal(0.2, s.ResistElemental, 9);

            items.HasShield = false;
            s = DerivedStatsCalculator.Compute(Config, new CharacterBuild(ClassId.Ninja, 99, default(PrimaryStats)), null, items);
            Assert.Equal(0, s.BlockChance, 9);
        }

        [Fact]
        public void NinjaWithBow_ScalesAtkWithFinesse_AndTemperAppliesToWeapon()
        {
            PrimaryStat scaling;
            Assert.True(WeaponLoadout.TryGetScalingStat("bow", out scaling));
            Assert.Equal(PrimaryStat.Finesse, scaling);
            Assert.True(WeaponLoadout.TryGetScalingStat("axe_2h", out scaling));
            Assert.Equal(PrimaryStat.Might, scaling);
            Assert.False(WeaponLoadout.TryGetScalingStat("rune_wand", out scaling));

            var bow = WeaponLoadout.PlainWeapon(Config, 10, "rare", 5, PrimaryStat.Finesse);
            Assert.Equal((4 + 16) * 1.12, bow.BaseAtk, 9);
            var s = DerivedStatsCalculator.Compute(Config, new CharacterBuild(ClassId.Ninja, 10, default(PrimaryStats)), bow, null);
            // Finesse at level 10 = 6 + 9 = 15; temper +5 = +30% base stats.
            Assert.Equal(20 * 1.12 * 1.30 + 15 * 0.8 + 10 * 0.5, s.Atk, 9);
        }

        [Fact]
        public void AttackSpeedModifier_Clamped()
        {
            Assert.Equal(0.80, DerivedStatsCalculator.ClampAttackSpeedModifier(Config, 0.5), 9);
            Assert.Equal(1.25, DerivedStatsCalculator.ClampAttackSpeedModifier(Config, 2.0), 9);
            Assert.Equal(1.10, DerivedStatsCalculator.ClampAttackSpeedModifier(Config, 1.10), 9);
        }
    }
}
