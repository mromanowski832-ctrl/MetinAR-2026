using System;
using System.Collections.Generic;
using MetinAR.Gameplay.Combat;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    /// <summary>
    /// METINAR-020 (S2a): HitDetected → DamageResolver → mob HP → death → EXP and talary, against the approved spec
    /// tables. Hand-checked reference: warrior level 1 with Miecz Rekruta (Atk 6) has Atk 6 + 6 × 0.8 + 0.5 = 11.3,
    /// Acc 74, CritChance 3.45%; the level 5 spider has HP 120, Def 15, Eva 5. HitChance = clamp(0.92 + 69 × 0.004 −
    /// 4 × 0.02) = 0.99, Mitigation = 15 / 73, so an average non-critical hit is 11.3 × 58/73 = 8.98 → 9.
    /// </summary>
    public class EncounterCombatSessionTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly ExpCurve Curve = new ExpCurve(Config);

        // Hit roll 0.5 (< 0.99), spread 0.5 (×1.00), crit roll 0.9 (no crit).
        private static readonly double[] PlainHit = { 0.5, 0.5, 0.9 };

        private static double[] Repeat(double[] pattern, int times, params double[] tail)
        {
            var list = new List<double>();
            for (int i = 0; i < times; i++) list.AddRange(pattern);
            list.AddRange(tail);
            return list.ToArray();
        }

        private static EncounterCombatSession SpiderSession(IGameplayRandom random, PlayerProgress player = null)
        {
            player = player ?? new PlayerProgress(Curve, ClassId.Warrior);
            return EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config), player, random);
        }

        [Fact]
        public void SpiderProfile_MatchesSpec()
        {
            var spider = MobCombatProfile.VenomousSpider(Config);
            Assert.Equal(MobCombatProfile.VenomousSpiderId, spider.Id);
            Assert.Equal(5, spider.Level);
            Assert.Equal(120, spider.MaxHp);   // MOBS_NPC.md §3, prototype HP unchanged
            Assert.Equal(24, spider.Atk);
            Assert.Equal(15, spider.Def);
            Assert.Equal(84, spider.BaseExp);
            Assert.Equal(10, spider.TalaryMin);
            Assert.Equal(18, spider.TalaryMax);
            Assert.Equal(0.15, spider.WeakPointCritBonus, 12);
            Assert.Equal(5, spider.Evasion, 12);      // S2 decision #41
            Assert.Equal(75, spider.Accuracy, 12);    // 70 + m
        }

        [Fact]
        public void NormalProfiles_MatchMobTable_AllLevels()
        {
            var t = CsvTable.Parse(TestData.RepoTable("mob_level_stats.csv"));
            Assert.Equal(99, t.Rows.Count);
            for (int i = 0; i < t.Rows.Count; i++)
            {
                int m = t.GetInt(i, "mobLevel");
                var p = MobCombatProfile.ForLevel(Config, "mob_test", m, MobRank.Normal, 0);
                Assert.True(t.GetInt(i, "normalHp") == p.MaxHp, "hp " + m);
                Assert.True(t.GetInt(i, "normalAtk") == p.Atk, "atk " + m);
                Assert.True(t.GetInt(i, "normalDef") == p.Def, "def " + m);
                Assert.True(t.GetInt(i, "normalExp") == p.BaseExp, "exp " + m);
                Assert.True(t.GetInt(i, "goldMin") == p.TalaryMin, "goldMin " + m);
                Assert.True(t.GetInt(i, "goldMax") == p.TalaryMax, "goldMax " + m);
            }
        }

        [Fact]
        public void RankProfiles_ApplyHpAndAtkMultipliers()
        {
            var elite = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Elite, 0);
            Assert.Equal(420, elite.MaxHp);   // round5(120 × 3.5)
            Assert.Equal(34, elite.Atk);      // round(24 × 1.4 = 33.6)
            Assert.Equal(15, elite.Def);
            var guardian = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Guardian, 0);
            Assert.Equal(1440, guardian.MaxHp);
            Assert.Equal(43, guardian.Atk);   // round(43.2)
        }

        [Fact]
        public void StarterWarrior_Level1_Stats()
        {
            var s = StarterLoadout.Stats(Config, ClassId.Warrior, 1);
            Assert.Equal(11.3, s.Atk, 9);
            Assert.Equal(74, s.Accuracy, 9);
            Assert.Equal(0.0345, s.CritChance, 9);
            Assert.Equal(1.6, s.CritMultiplier, 9);
        }

        [Fact]
        public void PlainHit_DealsNineDamage_AndUpdatesHp()
        {
            var rng = new ScriptedRandom(PlainHit);
            var session = SpiderSession(rng);
            CombatHitReport r = session.RegisterHit(1);
            Assert.Equal(CombatHitStatus.Resolved, r.Status);
            Assert.Equal(HitOutcome.Hit, r.Damage.Outcome);
            Assert.False(r.Damage.IsCritical);
            Assert.Equal(0.99, r.Damage.HitChance, 9);
            Assert.Equal(9, r.Damage.Damage);
            Assert.Equal(120, r.HpBefore);
            Assert.Equal(111, r.HpAfter);
            Assert.Equal(111, session.MobHp);
            Assert.False(r.Killed);
            Assert.Null(r.Reward);
            Assert.Equal(3, rng.Calls);
        }

        [Fact]
        public void WeakPointHit_AddsSpiderCritBonus()
        {
            // crit roll 0.1: above 3.45% but below 3.45% + 15% → critical: 11.3 × 1.6 × 58/73 = 14.37 → 14.
            var session = SpiderSession(new ScriptedRandom(0.5, 0.5, 0.1));
            CombatHitReport r = session.RegisterHit(1, weakPoint: true);
            Assert.True(r.Damage.IsCritical);
            Assert.Equal(14, r.Damage.Damage);

            var body = SpiderSession(new ScriptedRandom(0.5, 0.5, 0.1));
            Assert.False(body.RegisterHit(1).Damage.IsCritical);
        }

        [Fact]
        public void HitChanceRollFailure_IsMiss_NoDamage_SwingConsumed()
        {
            var session = SpiderSession(new ScriptedRandom(0.995));
            CombatHitReport r = session.RegisterHit(7);
            Assert.Equal(HitOutcome.Miss, r.Damage.Outcome);
            Assert.Equal(0, r.Damage.Damage);
            Assert.Equal(120, session.MobHp);
            Assert.Equal(CombatHitStatus.DuplicateSwing, session.RegisterHit(7).Status);
        }

        [Fact]
        public void SameSwing_HitsOnlyOnce()
        {
            var rng = new ScriptedRandom(Repeat(PlainHit, 2));
            var session = SpiderSession(rng);
            session.RegisterHit(3);
            CombatHitReport dup = session.RegisterHit(3);
            Assert.Equal(CombatHitStatus.DuplicateSwing, dup.Status);
            Assert.Equal(111, dup.HpAfter);
            Assert.Equal(3, rng.Calls); // no extra draws for the duplicate
            session.RegisterHit(4);
            Assert.Equal(102, session.MobHp);
        }

        [Fact]
        public void FourteenPlainHits_KillSpider_AwardExpAndTalaryOnce()
        {
            // 13 × 9 = 117, the 14th hit takes the last 3 HP; then one talary draw (0.5 → 10 + floor(4.5) = 14).
            var rng = new ScriptedRandom(Repeat(PlainHit, 14, 0.5));
            var player = new PlayerProgress(Curve, ClassId.Warrior);
            var session = SpiderSession(rng, player);
            CombatHitReport last = null;
            for (int swing = 1; swing <= 14; swing++)
            {
                last = session.RegisterHit(swing);
                Assert.Equal(swing == 14, last.Killed);
            }

            Assert.True(session.MobDefeated);
            Assert.Equal(0, session.MobHp);
            Assert.Equal(3, last.HpBefore);
            Assert.Equal(0, last.HpAfter);
            Assert.Equal(120, session.TotalDamage);
            Assert.Equal(14, session.HitsLanded);
            Assert.NotNull(last.Reward);
            // EXP: 84 × 1 (Normal) × 1.20 (mob 4 levels above) = 100.8 → 101.
            Assert.Equal(101, last.Reward.Value.Exp);
            Assert.Equal(1.20, last.Reward.Value.LevelDifferenceMultiplier, 12);
            Assert.Equal(14, last.Reward.Value.Talary);
            Assert.Equal(101, player.TotalExp);
            Assert.Equal(14, player.Talary);
            Assert.Equal(1, player.Level);
            Assert.Equal(0, last.LevelsGained);
            // HUD: 101 / 160 of level 1 = 63.1% → 2.525 of 4 orbs.
            Assert.Equal(2.525, player.FilledHudOrbs, 9);

            CombatHitReport after = session.RegisterHit(15);
            Assert.Equal(CombatHitStatus.MobAlreadyDefeated, after.Status);
            Assert.Null(after.Reward);
            Assert.Equal(101, player.TotalExp);
            Assert.Equal(14, player.Talary);
            Assert.Equal(43, rng.Calls);
        }

        [Fact]
        public void SeededFight_IsDeterministic_AndWithinSpecTtk()
        {
            int Fight(ulong seed, out long exp, out long talary)
            {
                var player = new PlayerProgress(Curve, ClassId.Warrior);
                var session = SpiderSession(new SeededGameplayRandom(seed), player);
                int swing = 0;
                while (!session.MobDefeated && swing < 100) session.RegisterHit(++swing);
                exp = player.TotalExp;
                talary = player.Talary;
                return swing;
            }

            for (ulong seed = 1; seed <= 200; seed++)
            {
                int a = Fight(seed, out long expA, out long talA);
                int b = Fight(seed, out long expB, out long talB);
                Assert.Equal(a, b);
                Assert.Equal(talA, talB);
                Assert.Equal(101, expA);
                Assert.InRange(talA, 10, 18);
                // Level 1 vs level 5 spider: 8–10 damage per hit, crits ×1.6, rare misses → 11–18 swings.
                Assert.InRange(a, 11, 18);
            }
        }

        [Fact]
        public void LevelUp_OnKill_NextFightUsesNewLevelStats()
        {
            var player = new PlayerProgress(Curve, ClassId.Warrior, totalExp: 100);
            var session = EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config), player,
                new ScriptedRandom(0.5, 0.5, 0.9, 0.0), startHp: 5);
            CombatHitReport kill = session.RegisterHit(1);
            Assert.True(kill.Killed);
            Assert.Equal(10, kill.Reward.Value.Talary); // roll 0 → minimum
            Assert.Equal(201, player.TotalExp);        // 160 to level 2
            Assert.Equal(1, kill.PlayerLevelBefore);
            Assert.Equal(2, kill.PlayerLevelAfter);
            Assert.Equal(1, kill.LevelsGained);
            Assert.Equal(41.0 / 270 * 4, player.FilledHudOrbs, 9); // level 2 needs 270

            // Level 2: Atk 6 + 7 × 0.8 + 1.0 = 12.6, Mitigation 15 / 81 → 10.27 → 10; EXP ×1.15 (3 above).
            var next = EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config), player, new ScriptedRandom(PlainHit));
            Assert.Equal(10, next.RegisterHit(1).Damage.Damage);
        }

        [Fact]
        public void StartHp_Validated()
        {
            var player = new PlayerProgress(Curve, ClassId.Warrior);
            var spider = MobCombatProfile.VenomousSpider(Config);
            Assert.Throws<ArgumentOutOfRangeException>(() => EncounterCombatSession.WithStarterLoadout(Config, spider, player, new ScriptedRandom(), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EncounterCombatSession.WithStarterLoadout(Config, spider, player, new ScriptedRandom(), 121));
        }
    }

    public class MobRewardCalculatorTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();

        [Theory]
        [InlineData(5, 1, 101)]    // 84 × 1.20 = 100.8
        [InlineData(5, 5, 84)]     // equal level
        [InlineData(5, 10, 84)]    // 5 below → ×1.0
        [InlineData(6, 1, 124)]    // 99 × 1.25 (cap) = 123.75
        [InlineData(2, 1, 44)]     // 42 × 1.05 = 44.1
        [InlineData(5, 11, 71)]    // 6 below: 84 × 0.85 = 71.4
        [InlineData(1, 20, 3)]     // 30 × floor 0.10
        public void Exp_NormalMob_SingleRoundingAtTheEnd(int mobLevel, int playerLevel, int expected)
        {
            var mob = MobCombatProfile.ForLevel(Config, "mob_test", mobLevel, MobRank.Normal, 0);
            Assert.Equal(expected, MobRewardCalculator.Exp(Config, mob, playerLevel));
        }

        [Fact]
        public void Exp_RankMultiplier_AppliesBeforeRounding()
        {
            var elite = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Elite, 0);
            var guardian = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Guardian, 0);
            Assert.Equal(252, MobRewardCalculator.Exp(Config, elite, 5));
            Assert.Equal(840, MobRewardCalculator.Exp(Config, guardian, 5));
            Assert.Equal(302, MobRewardCalculator.Exp(Config, elite, 1)); // 84 × 3 × 1.2 = 302.4
        }

        [Fact]
        public void Talary_UniformIntegerRange_InclusiveBounds()
        {
            var spider = MobCombatProfile.VenomousSpider(Config);
            Assert.Equal(10, MobRewardCalculator.RollTalary(Config, spider, new ScriptedRandom(0.0)));
            Assert.Equal(18, MobRewardCalculator.RollTalary(Config, spider, new ScriptedRandom(0.9999999)));
            Assert.Equal(14, MobRewardCalculator.RollTalary(Config, spider, new ScriptedRandom(0.5)));

            var seen = new HashSet<int>();
            var rng = new SeededGameplayRandom(42);
            for (int i = 0; i < 2000; i++)
            {
                int v = MobRewardCalculator.RollTalary(Config, spider, rng);
                Assert.InRange(v, 10, 18);
                seen.Add(v);
            }
            Assert.Equal(9, seen.Count);
        }

        [Fact]
        public void Talary_RankMultiplier_NoLevelDifference()
        {
            // ECONOMY.md §2 example at level 5 (avg 14): elite 42, guardian 140.
            var elite = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Elite, 0);
            var guardian = MobCombatProfile.ForLevel(Config, "mob_test", 5, MobRank.Guardian, 0);
            Assert.Equal(42, MobRewardCalculator.RollTalary(Config, elite, new ScriptedRandom(0.5)));
            Assert.Equal(140, MobRewardCalculator.RollTalary(Config, guardian, new ScriptedRandom(0.5)));
            MobKillReward r = MobRewardCalculator.Roll(Config, MobCombatProfile.VenomousSpider(Config), 1, new ScriptedRandom(0.5));
            Assert.Equal(14, r.Talary); // level difference ×1.2 does not touch talary
            Assert.Equal(101, r.Exp);
        }
    }

    public class PlayerProgressTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly ExpCurve Curve = new ExpCurve(Config);

        [Fact]
        public void AddExp_LevelsUpAcrossSeveralLevels()
        {
            var p = new PlayerProgress(Curve, ClassId.Warrior);
            Assert.Equal(1, p.Level);
            Assert.Equal(160, p.ExpToNextLevel);
            int gained = p.AddExp(Curve.CumulativeExpAtLevelStart(5) + 10);
            Assert.Equal(4, gained);
            Assert.Equal(5, p.Level);
            Assert.Equal(10, p.ExpIntoLevel);
            Assert.Equal(840, p.ExpToNextLevel);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.AddExp(-1));
        }

        [Fact]
        public void AddExp_CapsAtMaxLevel()
        {
            var p = new PlayerProgress(Curve, ClassId.Warrior, Curve.CumulativeExpAtLevelStart(98));
            int gained = p.AddExp(long.MaxValue / 4);
            Assert.Equal(1, gained);
            Assert.Equal(99, p.Level);
            Assert.True(p.IsMaxLevel);
            Assert.Equal(Curve.TotalExpToMaxLevel, p.TotalExp);
            Assert.Equal(4.0, p.FilledHudOrbs, 12);
            Assert.Equal(0, p.AddExp(500));
        }

        [Fact]
        public void Talary_Accumulates_NoNegative()
        {
            var p = new PlayerProgress(Curve, ClassId.Warrior);
            p.AddTalary(14);
            p.AddTalary(10);
            Assert.Equal(24, p.Talary);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.AddTalary(-1));
        }

        [Theory]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(2.525, 1, 1, 0.525, 0)]
        [InlineData(0.25, 0.25, 0, 0, 0)]
        [InlineData(4, 1, 1, 1, 1)]
        public void HudOrbs_FractionalFill(double filled, double o0, double o1, double o2, double o3)
        {
            Assert.Equal(o0, HudExpOrbs.OrbFill(filled, 0), 9);
            Assert.Equal(o1, HudExpOrbs.OrbFill(filled, 1), 9);
            Assert.Equal(o2, HudExpOrbs.OrbFill(filled, 2), 9);
            Assert.Equal(o3, HudExpOrbs.OrbFill(filled, 3), 9);
        }

        [Fact]
        public void HudOrbs_MatchExpPerHudOrbColumn()
        {
            var t = CsvTable.Parse(TestData.RepoTable("exp_curve.csv"));
            for (int i = 0; i < t.Rows.Count; i++)
            {
                int level = t.GetInt(i, "level");
                if (level >= Curve.MaxLevel) continue;
                long perOrb = t.GetLong(i, "expPerHudOrb");
                var p = new PlayerProgress(Curve, ClassId.Warrior, Curve.CumulativeExpAtLevelStart(level) + perOrb);
                Assert.Equal(level, p.Level);
                // One table orb of EXP fills one HUD orb (floor in the table → just under 1.0).
                Assert.InRange(p.FilledHudOrbs, 0.99, 1.0);
            }
        }
    }
}
