using System;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class DamageResolverTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly DamageResolver Resolver = new DamageResolver(Config);

        private static DamageRequest Basic(double power = 100, double def = 0, int attackerLevel = 10)
        {
            return new DamageRequest
            {
                AttackerLevel = attackerLevel, TargetLevel = attackerLevel, Power = power, TargetDefense = def,
                AttackerAccuracy = 100, TargetEvasion = 0, CritChance = 0.1, CritMultiplier = 1.6,
            };
        }

        [Theory]
        [InlineData(100, 0, 10, 10, 0.99)]   // 0.92 + 0.4 → clamp 0.99
        [InlineData(70, 60, 10, 10, 0.92 + 10 * 0.004)]
        [InlineData(70, 70, 10, 15, 0.92 - 5 * 0.02)]
        [InlineData(70, 70, 15, 10, 0.92)]   // only a higher target level penalises
        [InlineData(0, 200, 1, 30, 0.40)]    // clamp min
        public void HitChance_Formula(double acc, double eva, int attackerLevel, int targetLevel, double expected)
        {
            Assert.Equal(expected, Resolver.HitChance(acc, eva, attackerLevel, targetLevel), 9);
        }

        [Fact]
        public void Mitigation_DiminishingReturns_MagicalUsesHalfDef()
        {
            Assert.Equal(15.0 / (15 + 50 + 40), Resolver.Mitigation(15, 5, DamageKind.Physical), 12);
            Assert.Equal(7.5 / (7.5 + 50 + 40), Resolver.Mitigation(15, 5, DamageKind.Magical), 12);
            Assert.Equal(0, Resolver.Mitigation(0, 5, DamageKind.Physical), 12);
            Assert.Equal(0, Resolver.Mitigation(-10, 5, DamageKind.Physical), 12);
        }

        [Fact]
        public void GeometricMiss_NoRollsNoDamage()
        {
            var req = Basic();
            req.GeometricHit = false;
            var rng = new ScriptedRandom();
            var r = Resolver.Resolve(req, rng);
            Assert.Equal(HitOutcome.Miss, r.Outcome);
            Assert.Equal(0, r.Damage);
            Assert.Equal(0, rng.Calls);
        }

        [Fact]
        public void FailedHitRoll_IsMiss()
        {
            var req = Basic();
            req.AttackerAccuracy = 70; req.TargetEvasion = 70; // 0.92
            var r = Resolver.Resolve(req, new ScriptedRandom(0.95));
            Assert.Equal(HitOutcome.Miss, r.Outcome);
            Assert.Equal(0.92, r.HitChance, 9);
            Assert.False(r.Landed);
        }

        [Fact]
        public void PlainHit_NeutralRolls_EqualsRawTimesMitigation()
        {
            var req = Basic(power: 100, def: 15, attackerLevel: 5);
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99));
            Assert.Equal(HitOutcome.Hit, r.Outcome);
            Assert.Equal(100 * (1 - 15.0 / 105), r.UnroundedDamage, 9);
            Assert.Equal(86, r.Damage);
        }

        [Fact]
        public void Spread_IsWithin092And108()
        {
            var low = Resolver.Resolve(Basic(), new ScriptedRandom(0.0, 0.0, 0.99));
            var high = Resolver.Resolve(Basic(), new ScriptedRandom(0.0, 1.0, 0.99));
            Assert.Equal(92, low.UnroundedDamage, 9);
            Assert.Equal(108, high.UnroundedDamage, 9);
        }

        [Fact]
        public void Crit_UsesMultiplierCappedAt22_PlusWeakPointChance()
        {
            var req = Basic();
            req.CritChance = 0.05; req.WeakPointCritBonus = 0.15; req.CritMultiplier = 3.0;
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.19)); // 0.19 < 0.20
            Assert.True(r.IsCritical);
            Assert.Equal(220, r.Damage);

            r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.21));
            Assert.False(r.IsCritical);
            Assert.Equal(100, r.Damage);
        }

        [Fact]
        public void NoCritAllowed_SkipsCritRoll()
        {
            var req = Basic();
            req.CanCrit = false; req.SkipHitRoll = true; req.ApplyVariance = false; // DoT tick
            var rng = new ScriptedRandom();
            var r = Resolver.Resolve(req, rng);
            Assert.Equal(100, r.Damage);
            Assert.Equal(0, rng.Calls);
            Assert.True(double.IsNaN(r.HitChance));
        }

        [Fact]
        public void Block_Minus60Percent_AndParry_Zero()
        {
            var req = Basic();
            req.TargetCanBlock = true; req.TargetBlockChance = 0.3;
            var blocked = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.29, 0.5, 0.99));
            Assert.Equal(HitOutcome.Blocked, blocked.Outcome);
            Assert.Equal(40, blocked.Damage);
            Assert.True(blocked.Landed);

            var notBlocked = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.31, 0.5, 0.99));
            Assert.Equal(HitOutcome.Hit, notBlocked.Outcome);
            Assert.Equal(100, notBlocked.Damage);

            req.TargetParryWindow = true;
            var parried = Resolver.Resolve(req, new ScriptedRandom(0.0));
            Assert.Equal(HitOutcome.Parried, parried.Outcome);
            Assert.Equal(0, parried.Damage);
        }

        [Fact]
        public void BlockChance_CappedAt35_AndOnlyWhenTargetCanBlock()
        {
            var req = Basic();
            req.TargetCanBlock = true; req.TargetBlockChance = 0.9;
            Assert.Equal(HitOutcome.Hit, Resolver.Resolve(req, new ScriptedRandom(0.0, 0.36, 0.5, 0.99)).Outcome);
            req.TargetCanBlock = false;
            Assert.Equal(HitOutcome.Hit, Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99)).Outcome);
        }

        [Fact]
        public void Magical_HalfDef_AndResistanceCappedAt50()
        {
            var req = Basic(power: 100, def: 100, attackerLevel: 10);
            req.Kind = DamageKind.Magical; req.Type = DamageType.Dark; req.TargetResistance = 0.8;
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99));
            double mitigation = 50.0 / (50 + 50 + 80);
            Assert.Equal(100 * (1 - mitigation) * 0.5, r.UnroundedDamage, 9);
        }

        [Fact]
        public void Modifiers_SumInsideGroup_MultiplyAcrossGroups()
        {
            var req = Basic();
            req.ModifierGroups.Add(new[] { 0.10, -0.15 });
            req.ModifierGroups.Add(new[] { 0.05 });
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99));
            Assert.Equal(100 * 0.95 * 1.05, r.UnroundedDamage, 9);
            Assert.Equal(0, DamageResolver.ModifierMultiplier(new[] { new[] { -2.0 } }), 9);
        }

        [Fact]
        public void SkillCoefficientAndFlat()
        {
            var req = Basic();
            req.Coefficient = 1.6 * Config.SkillTiers.CoefMultiplier(10); req.Flat = 7;
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99));
            Assert.Equal(100 * 1.6 * 1.54 + 7, r.UnroundedDamage, 9);
        }

        [Fact]
        public void Minimum1_OnLandedHits()
        {
            var req = Basic(power: 0.2);
            var r = Resolver.Resolve(req, new ScriptedRandom(0.0, 0.5, 0.99));
            Assert.Equal(1, r.Damage);
        }

        [Fact]
        public void SeededRandom_IsDeterministic_AndHitRateMatchesHitChance()
        {
            var a = new SeededGameplayRandom(42);
            var b = new SeededGameplayRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                double x = a.NextDouble();
                Assert.Equal(x, b.NextDouble());
                Assert.InRange(x, 0.0, 0.9999999999999999);
            }

            var rng = new SeededGameplayRandom(2026);
            var req = Basic();
            req.AttackerAccuracy = 70; req.TargetEvasion = 70; // 0.92
            int hits = 0, crits = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                var r = Resolver.Resolve(req, rng);
                if (r.Landed) hits++;
                if (r.IsCritical) crits++;
                if (r.Landed) Assert.InRange(r.Damage, 92, 108 * 1.6 + 1);
            }
            Assert.InRange(hits / (double)n, 0.91, 0.93);
            Assert.InRange(crits / (double)hits, 0.09, 0.11);
        }

        [Fact]
        public void NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => Resolver.Resolve(null, new ScriptedRandom()));
            Assert.Throws<ArgumentNullException>(() => Resolver.Resolve(Basic(), null));
            Assert.Throws<ArgumentNullException>(() => new DamageResolver(null));
        }
    }
}
