using System;
using System.Collections.Generic;
using MetinAR.ArEncounter;
using MetinAR.Gameplay.Combat;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    /// <summary>
    /// METINAR-039 (#82): warrior combo L–P–L for 1H and 2H — state sequence, window reset, end after cios 3, tap vs hold,
    /// hits only from blade geometry (1 per target per swing, none out of reach), per-step damage through the S1 formulas,
    /// weapon switch hooks. Compiles the production WarriorCombo*.cs and FppHitGeometry.cs.
    /// </summary>
    public class WarriorComboTests
    {
        private const double Dt = 0.02;
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly FppPoint BoxMin = new FppPoint(-0.285, 0.125, -0.605);
        private static readonly FppPoint BoxMax = new FppPoint(0.285, 0.595, 0.345);

        private static WarriorComboMachine Machine(WeaponComboConfig weapon = null)
        {
            return new WarriorComboMachine(weapon ?? WeaponComboConfig.Default1H());
        }

        private static ComboInputResult Tap(WarriorComboMachine m)
        {
            m.PressAttack(true);
            return m.ReleaseAttack(true);
        }

        /// <summary>Ticks in ≤ Dt steps for exactly <paramref name="seconds"/>.</summary>
        private static void Run(WarriorComboMachine m, double seconds, Action<WarriorComboMachine> perTick = null)
        {
            double left = seconds;
            while (left > 1e-12)
            {
                double d = Math.Min(Dt, left);
                m.Tick(d, true);
                perTick?.Invoke(m);
                left -= d;
            }
        }

        private static void RunUntilPhaseChanges(WarriorComboMachine m, Action<WarriorComboMachine> perTick = null)
        {
            ComboPhase start = m.Phase;
            int swing = m.SwingId;
            for (int i = 0; i < 1000 && m.Phase == start && m.SwingId == swing; i++)
            {
                m.Tick(Dt, true);
                perTick?.Invoke(m);
            }
        }

        /// <summary>
        /// Synthetic blade pose in the target frame: grip <paramref name="distance"/> m in front of the body box, blade sweeping
        /// horizontally from −60° to +60° across the step's hit interval (straight at the body in the middle).
        /// </summary>
        private static Func<double, bool> SweepingBlade(WarriorComboMachine m, double distance)
        {
            return clip =>
            {
                ComboStepConfig s = m.Weapon.Step(m.HitStep);
                double f = (clip - s.HitStartSeconds) / (s.HitEndSeconds - s.HitStartSeconds);
                double angle = (-60 + 120 * f) * Math.PI / 180;
                var origin = new FppPoint(0, 0.3, BoxMin.Z - distance);
                return ComboBladeProbe.Intersects(m.Weapon, origin, new FppPoint(Math.Sin(angle), 0, Math.Cos(angle)), BoxMin, BoxMax);
            };
        }

        // ---------- configuration ----------

        [Fact]
        public void Defaults_AreValid_AndFollowTheSpecShape()
        {
            WeaponComboConfig one = WeaponComboConfig.Default1H(), two = WeaponComboConfig.Default2H();
            Assert.Empty(one.Validate());
            Assert.Empty(two.Validate());
            foreach (var w in new[] { one, two })
            {
                Assert.Equal(3, w.Steps.Length);
                Assert.Equal(ComboSwingArc.LeftToRight, w.Step(1).Arc);
                Assert.Equal(ComboSwingArc.RightToLeft, w.Step(2).Arc);
                Assert.Equal(ComboSwingArc.Finisher, w.Step(3).Arc);
                Assert.True(w.Step(3).DamageMultiplier > w.Step(1).DamageMultiplier, "cios 3 is the stronger finisher");
                Assert.Equal(0.6, w.HoldThresholdSeconds);
                Assert.Equal(0.15, w.InputBufferSeconds);
            }
            Assert.Equal("1H", one.Label);
            Assert.Equal("2H", two.Label);
            Assert.True(one.UsesItemType("sword_1h"));
            Assert.True(two.UsesItemType("sword_2h") && two.UsesItemType("axe_2h"));
            Assert.False(two.UsesItemType("sword_1h"));
        }

        [Fact]
        public void TwoHanded_IsSlowerStrongerWider_NoShield_BothHands()
        {
            WeaponComboConfig one = WeaponComboConfig.Default1H(), two = WeaponComboConfig.Default2H();
            for (int s = 1; s <= 3; s++)
            {
                Assert.True(two.SwingRealSeconds(s) > one.SwingRealSeconds(s), "2H slower at step " + s);
                Assert.True(two.Step(s).DamageMultiplier > one.Step(s).DamageMultiplier, "2H stronger at step " + s);
            }
            Assert.True(two.BladeTipMeters > one.BladeTipMeters, "2H has its own longer reach");
            Assert.False(two.AllowsShield);
            Assert.True(one.AllowsShield);
            Assert.Equal(ComboHandLayout.BothHandsOnGrip, two.HandLayout);
            Assert.Equal(ComboHandLayout.RightHandGrip, one.HandLayout);
            Assert.NotEqual(one.AnimationSetId, two.AnimationSetId);
        }

        [Fact]
        public void OneHanded_MatchesCurrentFppTimingAndReach()
        {
            WeaponComboConfig one = WeaponComboConfig.Default1H();
            Assert.Equal(FppCombatTimeline.AttackSeconds, one.Step(1).SwingSeconds, 6);
            Assert.Equal(FppCombatTimeline.HitStartSeconds, one.Step(1).HitStartSeconds, 6);
            Assert.Equal(FppCombatTimeline.HitEndSeconds, one.Step(1).HitEndSeconds, 6);
            Assert.Equal(0.13 * 0.75, one.BladeBaseMeters, 9);
            Assert.Equal(0.94 * 0.75, one.BladeTipMeters, 9);
        }

        [Fact]
        public void Validate_RejectsBrokenConfigs()
        {
            var w = WeaponComboConfig.Default2H(); w.AllowsShield = true;
            Assert.Contains("2H must not allow a shield", w.Validate());
            w = WeaponComboConfig.Default1H(); w.Steps[0].Arc = ComboSwingArc.RightToLeft;
            Assert.NotEmpty(w.Validate());
            w = WeaponComboConfig.Default1H(); w.Steps[1].HitEndSeconds = 5;
            Assert.Contains("Steps[1].HitWindow", w.Validate());
            w = WeaponComboConfig.Default1H(); w.Steps = new[] { w.Steps[0], w.Steps[1] };
            Assert.NotEmpty(w.Validate());
            w = WeaponComboConfig.Default1H(); w.InputBufferSeconds = 0.6;
            Assert.NotEmpty(w.Validate());
            w = WeaponComboConfig.Default1H(); w.AttackSpeed = 0;
            Assert.Contains("AttackSpeed", w.Validate());
            w = WeaponComboConfig.Default1H(); w.BladeTipMeters = w.BladeBaseMeters;
            Assert.Contains("Blade", w.Validate());
            w = WeaponComboConfig.Default1H(); w.Steps[2].DamageMultiplier = double.NaN;
            Assert.Contains("Steps[2].DamageMultiplier", w.Validate());
            Assert.Throws<ArgumentException>(() => new WarriorComboMachine(w));
        }

        // ---------- state sequence ----------

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FullCombo_StepsOneTwoThree_ThenEnds(int kind)
        {
            var weapon = kind == 1 ? WeaponComboConfig.Default1H() : WeaponComboConfig.Default2H();
            var m = Machine(weapon);
            var started = new List<int>();
            int completed = 0, resets = 0;
            m.StepStarted += (step, swing) => started.Add(step);
            m.ComboCompleted += () => completed++;
            m.ComboReset += () => resets++;

            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(ComboInputResult.StartedStep, Tap(m));
            Assert.Equal(1, m.Step);
            for (int step = 1; step <= 3; step++)
            {
                Assert.Equal(ComboPhase.Swing, m.Phase);
                Assert.Equal(step, m.Step);
                Assert.Equal(step, m.SwingId);
                RunUntilPhaseChanges(m);
                if (step < 3)
                {
                    Assert.Equal(ComboPhase.Window, m.Phase);
                    Assert.Equal(step, m.Step);
                    Assert.Equal(weapon.ComboWindowSeconds, m.WindowRemaining, 9);
                    Run(m, 0.1);
                    Assert.Equal(ComboInputResult.StartedStep, Tap(m));
                }
            }
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(0, m.Step);
            Assert.Equal(new[] { 1, 2, 3 }, started);
            Assert.Equal(1, completed);
            Assert.Equal(0, resets);

            // After cios 3 the combo is over: the next tap is a fresh cios 1, and no window is waiting.
            Run(m, 1.0);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(ComboInputResult.StartedStep, Tap(m));
            Assert.Equal(1, m.Step);
            Assert.Equal(4, m.SwingId);
        }

        [Fact]
        public void SwingDurations_FollowConfigAndSpeed()
        {
            var two = Machine(WeaponComboConfig.Default2H());
            Tap(two);
            Run(two, 0.80);
            Assert.Equal(ComboPhase.Swing, two.Phase); // 1H would be done at 0.72 s, 2H swings 1.00 s
            Run(two, 0.20);
            Assert.Equal(ComboPhase.Window, two.Phase);

            var fast = WeaponComboConfig.Default1H();
            fast.AttackSpeed = 2.0;
            var m = Machine(fast);
            Tap(m);
            Run(m, 0.34);
            Assert.Equal(ComboPhase.Swing, m.Phase);
            Assert.Equal(0.68, m.ClipTime, 6);
            Run(m, 0.02);
            Assert.Equal(ComboPhase.Window, m.Phase);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void NoTapInWindow_ResetsToStepOne(int fromStep)
        {
            var m = Machine();
            int resets = 0;
            m.ComboReset += () => resets++;
            Tap(m);
            RunUntilPhaseChanges(m);
            if (fromStep == 2) { Tap(m); RunUntilPhaseChanges(m); }
            Assert.Equal(ComboPhase.Window, m.Phase);
            Assert.Equal(fromStep, m.Step);
            Run(m, m.Weapon.ComboWindowSeconds - 0.04);
            Assert.Equal(ComboPhase.Window, m.Phase);
            Run(m, 0.06);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(0, m.Step);
            Assert.Equal(1, resets);
            Tap(m);
            Assert.Equal(1, m.Step);
        }

        [Fact]
        public void TapMidSwing_IsIgnored_TapInBuffer_ChainsAtSwingEnd()
        {
            var m = Machine();
            Tap(m);
            Run(m, 0.30);
            Assert.Equal(ComboInputResult.Ignored, Tap(m));
            Assert.Equal(1, m.Step);
            Assert.Equal(1, m.SwingId);
            Run(m, 0.30); // 0.60 s: 0.12 s left of 0.72 s ≤ 0.15 s buffer
            Assert.Equal(ComboInputResult.BufferedStep, Tap(m));
            Assert.Equal(1, m.Step);
            Run(m, 0.12);
            Assert.Equal(ComboPhase.Swing, m.Phase);
            Assert.Equal(2, m.Step);
            Assert.Equal(2, m.SwingId);
        }

        [Fact]
        public void TapDuringFinisher_IsIgnored_ComboEnds()
        {
            var m = Machine();
            Tap(m); RunUntilPhaseChanges(m);
            Tap(m); RunUntilPhaseChanges(m);
            Tap(m);
            Assert.Equal(3, m.Step);
            Run(m, 0.70); // inside the last 0.15 s of the 0.80 s finisher
            Assert.Equal(ComboInputResult.Ignored, Tap(m));
            RunUntilPhaseChanges(m);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(3, m.SwingId);
        }

        [Fact]
        public void RepeatedTapsCannotSkipOrRestartSteps()
        {
            var m = Machine();
            Tap(m);
            for (int i = 0; i < 20; i++) Tap(m);
            Assert.Equal(1, m.Step);
            Assert.Equal(1, m.SwingId);
        }

        [Fact]
        public void PressStartedInWindow_KeepsTheWindowUntilRelease()
        {
            var m = Machine();
            Tap(m);
            RunUntilPhaseChanges(m);
            Run(m, m.Weapon.ComboWindowSeconds - 0.02);
            m.PressAttack(true);
            Run(m, 0.30); // the window would have expired; the press began inside it
            Assert.Equal(ComboPhase.Window, m.Phase);
            Assert.Equal(ComboInputResult.StartedStep, m.ReleaseAttack(true));
            Assert.Equal(2, m.Step);
        }

        [Fact]
        public void StallOrNotReady_CancelsTheCombo()
        {
            var m = Machine();
            Tap(m); RunUntilPhaseChanges(m); Tap(m);
            Assert.Equal(2, m.Step);
            m.Tick(0.3, true);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Tap(m);
            Assert.Equal(1, m.Step);
            m.Tick(0.02, false);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            m.Tick(double.NaN, true);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            m.PressAttack(false);
            Assert.Equal(ComboInputResult.None, m.ReleaseAttack(true));
        }

        // ---------- tap vs hold ----------

        [Fact]
        public void Hold_FiresHeavySlash_NeverAComboSwing()
        {
            var m = Machine();
            m.ChargedSkill = ChargedSkillSlot.WarriorHeavySlash();
            var fired = new List<string>();
            int starts = 0;
            m.ChargedSkillFired += fired.Add;
            m.StepStarted += (s, id) => starts++;
            m.PressAttack(true);
            Run(m, 0.58);
            Assert.Empty(fired);
            Run(m, 0.04);
            Assert.Equal(new[] { "warrior_heavy_slash" }, fired);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Run(m, 1.0);
            Assert.Equal(0, starts);
            Assert.Equal(0, m.SwingId);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.False(m.SampleBlade(1, t => true, out _));
        }

        [Fact]
        public void HoldFromWindow_FiresSkill_AndResetsComboToStepOne()
        {
            var m = Machine();
            m.ChargedSkill = ChargedSkillSlot.WarriorHeavySlash();
            int fired = 0;
            m.ChargedSkillFired += id => fired++;
            Tap(m); RunUntilPhaseChanges(m);
            Assert.Equal(ComboPhase.Window, m.Phase);
            m.PressAttack(true);
            Run(m, 0.62);
            Assert.Equal(1, fired);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Assert.Equal(1, m.SwingId);
            Tap(m);
            Assert.Equal(1, m.Step);
        }

        [Fact]
        public void Hold_FiresHeavySlash_ByDefault_WithoutAnyAssignment()
        {
            var m = Machine(); // no ChargedSkill set: spec §4 binds hold ATAK to Ciężkie Cięcie directly
            var fired = new List<string>();
            m.ChargedSkillFired += fired.Add;
            m.PressAttack(true);
            Run(m, 0.62);
            Assert.Equal(new[] { "warrior_heavy_slash" }, fired);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
        }

        [Fact]
        public void Hold_WhenCostOrCooldownCheckFails_FiresNothing()
        {
            var m = Machine();
            var slot = ChargedSkillSlot.WarriorHeavySlash();
            string asked = null;
            slot.CanCast = id => { asked = id; return false; };
            m.ChargedSkill = slot;
            int fired = 0, unavailable = 0, starts = 0;
            m.ChargedSkillFired += id => fired++;
            m.ChargedSkillUnavailable += () => unavailable++;
            m.StepStarted += (s, id) => starts++;
            m.PressAttack(true);
            Run(m, 0.7);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Assert.Equal("warrior_heavy_slash", asked);
            Assert.Equal(0, fired);
            Assert.Equal(1, unavailable);
            Assert.Equal(0, starts);
            Assert.Equal(ComboPhase.Idle, m.Phase);
        }

        [Fact]
        public void ReleaseJustBeforeThreshold_IsATap()
        {
            var m = Machine();
            m.ChargedSkill = ChargedSkillSlot.WarriorHeavySlash();
            int fired = 0;
            m.ChargedSkillFired += id => fired++;
            m.PressAttack(true);
            Run(m, 0.58);
            Assert.Equal(ComboInputResult.StartedStep, m.ReleaseAttack(true));
            Assert.Equal(1, m.Step);
            Assert.Equal(0, fired);
        }

        [Fact]
        public void IgnoredMidSwingPress_CannotBecomeAChargedSkill()
        {
            var m = Machine();
            m.ChargedSkill = ChargedSkillSlot.WarriorHeavySlash();
            int fired = 0;
            m.ChargedSkillFired += id => fired++;
            Tap(m);
            Run(m, 0.1);
            m.PressAttack(true);
            Run(m, 0.7);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Assert.Equal(0, fired);
        }

        // ---------- hits from the blade ----------

        [Fact]
        public void PressingTheButton_NeverRegistersAHit_WithoutBladeContact()
        {
            var m = Machine();
            int hits = 0;
            Tap(m);
            Run(m, 0.72, x => { if (x.SampleBlade(1, t => false, out _)) hits++; });
            Assert.Equal(0, hits);
            Assert.False(m.SampleBlade(1, null, out _));
        }

        [Fact]
        public void BladeIsSampledOnlyInsideTheStepHitInterval_AtMostTenMsApart()
        {
            var m = Machine(WeaponComboConfig.Default2H());
            var times = new List<double>();
            Tap(m);
            Run(m, 1.0, x => x.SampleBlade(1, t => { times.Add(t); return false; }, out _));
            Assert.NotEmpty(times);
            ComboStepConfig s = m.Weapon.Step(1);
            Assert.All(times, t => Assert.InRange(t, s.HitStartSeconds, s.HitEndSeconds));
            times.Sort();
            Assert.Equal(s.HitStartSeconds, times[0], 9);
            Assert.Equal(s.HitEndSeconds, times[times.Count - 1], 9);
            for (int i = 1; i < times.Count; i++) Assert.True(times[i] - times[i - 1] <= 0.01 + 1e-9);
        }

        [Fact]
        public void DroppedFrame_StillSamplesTheCrossedInterval()
        {
            var m = Machine();
            Tap(m);
            m.Tick(0.15, true);
            Assert.False(m.HasHitWindow);
            m.Tick(0.25, true);
            Assert.True(m.HasHitWindow);
            Assert.Equal(0.18, m.HitWindowStart, 9);
            Assert.Equal(0.40, m.HitWindowEnd, 9);
            Assert.True(m.SampleBlade(5, t => t > 0.3, out ComboHit hit));
            Assert.InRange(hit.ClipTime, 0.3, 0.32);
        }

        [Fact]
        public void OneHitPerTargetPerSwing_EachStepRearms()
        {
            var m = Machine();
            var hits = new List<ComboHit>();
            Action<WarriorComboMachine> sample = x =>
            {
                foreach (int target in new[] { 1, 2 })
                    if (x.SampleBlade(target, t => true, out ComboHit h)) hits.Add(h);
            };
            Tap(m);
            for (int step = 1; step <= 3; step++)
            {
                RunUntilPhaseChanges(m, sample);
                if (step < 3) Tap(m);
            }
            Assert.Equal(6, hits.Count);
            for (int step = 1; step <= 3; step++)
            {
                var ofStep = hits.FindAll(h => h.Step == step);
                Assert.Equal(2, ofStep.Count);
                Assert.Contains(ofStep, h => h.TargetId == 1);
                Assert.Contains(ofStep, h => h.TargetId == 2);
                Assert.All(ofStep, h => Assert.Equal(step, h.SwingId));
                Assert.All(ofStep, h => Assert.Equal(m.Weapon.Step(step).DamageMultiplier, h.DamageMultiplier));
                Assert.All(ofStep, h => Assert.Equal(ComboWeaponKind.OneHanded, h.Weapon));
            }
        }

        [Fact]
        public void BufferedChain_DoesNotLetTheNextSwingReuseTheOldInterval()
        {
            var m = Machine();
            var hits = new List<ComboHit>();
            Tap(m);
            Run(m, 0.60, x => { if (x.SampleBlade(1, t => true, out ComboHit h)) hits.Add(h); });
            Tap(m); // buffered
            Run(m, 0.12 + 0.10, x => { if (x.SampleBlade(1, t => true, out ComboHit h)) hits.Add(h); });
            Assert.Equal(2, m.Step);
            Assert.Single(hits); // step 2 at 0.10 s has not reached its 0.18 s hit start yet
            Run(m, 0.20, x => { if (x.SampleBlade(1, t => true, out ComboHit h)) hits.Add(h); });
            Assert.Equal(2, hits.Count);
            Assert.Equal(2, hits[1].SwingId);
        }

        [Fact]
        public void Reach_OutOfRangeTargetIsNeverHit_TwoHandedReachesFurther()
        {
            WeaponComboConfig one = WeaponComboConfig.Default1H(), two = WeaponComboConfig.Default2H();
            var straight = new FppPoint(0, 0, 1);
            Func<double, FppPoint> at = d => new FppPoint(0, 0.3, BoxMin.Z - d);
            Assert.True(ComboBladeProbe.Intersects(one, at(0.5), straight, BoxMin, BoxMax));
            Assert.True(ComboBladeProbe.Intersects(two, at(0.5), straight, BoxMin, BoxMax));
            Assert.False(ComboBladeProbe.Intersects(one, at(0.8), straight, BoxMin, BoxMax));
            Assert.True(ComboBladeProbe.Intersects(two, at(0.8), straight, BoxMin, BoxMax));
            Assert.False(ComboBladeProbe.Intersects(two, at(1.0), straight, BoxMin, BoxMax));
            Assert.False(ComboBladeProbe.Intersects(one, at(0.5), new FppPoint(0, 0, -1), BoxMin, BoxMax)); // pointing away
            Assert.False(ComboBladeProbe.Intersects(one, at(0.5), new FppPoint(0, 0, 0), BoxMin, BoxMax));
            Assert.False(ComboBladeProbe.Intersects(one, new FppPoint(double.NaN, 0, 0), straight, BoxMin, BoxMax));
            Assert.False(ComboBladeProbe.Intersects(null, at(0.5), straight, BoxMin, BoxMax));
        }

        [Theory]
        [InlineData(1, 0.5, 3)]
        [InlineData(1, 0.8, 0)]
        [InlineData(2, 0.8, 3)]
        [InlineData(2, 1.0, 0)]
        public void FullCombo_WithSweepingBlade_HitsOnlyWithinReach(int kind, double distance, int expectedHits)
        {
            var m = Machine(kind == 1 ? WeaponComboConfig.Default1H() : WeaponComboConfig.Default2H());
            var hits = new List<ComboHit>();
            Action<WarriorComboMachine> sample = x => { if (x.SampleBlade(7, SweepingBlade(x, distance), out ComboHit h)) hits.Add(h); };
            Tap(m);
            for (int step = 1; step <= 3; step++)
            {
                RunUntilPhaseChanges(m, sample);
                if (step < 3) Tap(m);
            }
            Assert.Equal(expectedHits, hits.Count);
            for (int i = 0; i < hits.Count; i++) Assert.Equal(i + 1, hits[i].Step);
        }

        // ---------- damage per step (S1 formulas) ----------

        [Fact]
        public void DamagePerStep_UsesConfiguredMultipliersThroughS1Formulas()
        {
            // Warrior level 1 + Miecz Rekruta vs level 5 spider: plain hit 11.3 × 58/73 = 8.978 (EncounterCombatSessionTests).
            // ×1.0 → 9, ×1.3 → 11.67 → 12 (1H finisher); 2H: ×1.4 → 12.57 → 13, ×1.8 → 16.16 → 16.
            Assert.Equal(new[] { 9, 9, 12 }, ComboDamage(WeaponComboConfig.Default1H()));
            Assert.Equal(new[] { 13, 13, 16 }, ComboDamage(WeaponComboConfig.Default2H()));
        }

        private static int[] ComboDamage(WeaponComboConfig weapon)
        {
            var curve = new ExpCurve(Config);
            var session = EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config),
                new PlayerProgress(curve, ClassId.Warrior), new ScriptedRandom(0.5, 0.5, 0.9, 0.5, 0.5, 0.9, 0.5, 0.5, 0.9));
            var m = Machine(weapon);
            var damage = new List<int>();
            Action<WarriorComboMachine> sample = x =>
            {
                if (!x.SampleBlade(42, t => true, out ComboHit hit)) return;
                CombatHitReport r = session.RegisterHit(hit.SwingId, false, hit.DamageMultiplier);
                Assert.Equal(CombatHitStatus.Resolved, r.Status);
                Assert.Equal(r.HpBefore - r.Damage.Damage, r.HpAfter);
                damage.Add(r.Damage.Damage);
            };
            Tap(m);
            for (int step = 1; step <= 3; step++)
            {
                RunUntilPhaseChanges(m, sample);
                if (step < 3) Tap(m);
            }
            Assert.Equal(3, session.HitsLanded);
            Assert.Equal(session.MobMaxHp - session.MobHp, session.TotalDamage);
            return damage.ToArray();
        }

        [Fact]
        public void Session_RejectsSecondHitOfTheSameSwing_AndBadCoefficients()
        {
            var session = EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config),
                new PlayerProgress(new ExpCurve(Config), ClassId.Warrior), new ScriptedRandom(0.5, 0.5, 0.9));
            Assert.Equal(CombatHitStatus.Resolved, session.RegisterHit(1, false, 1.3).Status);
            Assert.Equal(CombatHitStatus.DuplicateSwing, session.RegisterHit(1, false, 1.3).Status);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RegisterHit(2, false, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RegisterHit(2, false, double.NaN));
        }

        // ---------- weapon switch ----------

        [Fact]
        public void WeaponSwitch_CancelsCombo_AndExposesTheNewHooks()
        {
            var m = Machine();
            WeaponComboConfig seen = null;
            m.WeaponChanged += w => seen = w;
            Tap(m); RunUntilPhaseChanges(m); Tap(m);
            Assert.Equal(2, m.Step);

            m.SetWeapon(WeaponComboConfig.Default2H());
            Assert.NotNull(seen);
            Assert.Equal(ComboWeaponKind.TwoHanded, seen.Kind);
            Assert.Equal(ComboHandLayout.BothHandsOnGrip, seen.HandLayout);
            Assert.Equal("warrior_combo_2h", seen.AnimationSetId);
            Assert.False(seen.AllowsShield);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Tap(m);
            Assert.Equal(1, m.Step);
            Run(m, 0.80);
            Assert.Equal(ComboPhase.Swing, m.Phase); // 2H timing now

            m.SetWeapon(WeaponComboConfig.Default1H());
            Assert.Equal("starter_sword", seen.ModelId);
            Assert.Equal(ComboHandLayout.RightHandGrip, seen.HandLayout);
            Assert.Equal("SFX_Combat_SwordSwing_01", seen.SwingSoundId);
            Assert.Equal("SFX_Combat_SwordHit_01", seen.HitSoundId);
            Assert.Throws<ArgumentNullException>(() => m.SetWeapon(null));
        }

        [Fact]
        public void Machine_CopiesConfig_SoLaterEditsCannotChangeARunningCombo()
        {
            var cfg = WeaponComboConfig.Default1H();
            var m = Machine(cfg);
            cfg.Steps[0].SwingSeconds = 10;
            cfg.ComboWindowSeconds = 99;
            Tap(m);
            RunUntilPhaseChanges(m);
            Assert.Equal(ComboPhase.Window, m.Phase);
            Assert.Equal(0.35, m.WindowRemaining, 9);
        }

        [Fact]
        public void LogLine_MatchesSmokeFormat()
        {
            Assert.Equal("[Combo] step=1 weapon=1H hit=yes swing=4", ComboLog.Line(1, ComboWeaponKind.OneHanded, true, 4));
            Assert.Equal("[Combo] step=3 weapon=2H hit=no swing=9", ComboLog.Line(3, ComboWeaponKind.TwoHanded, false, 9));
        }
    
        // ---------- QA log lines (#100 collector, #90 protocol) ----------

        private static List<string> Logged(WarriorComboMachine m, out ComboStepLogger logger)
        {
            var lines = new List<string>();
            logger = new ComboStepLogger(m, lines.Add);
            return lines;
        }

        [Fact]
        public void QaLog_ExactStrings()
        {
            Assert.Equal("[Combo] step=1", ComboLog.Step(1));
            Assert.Equal("[Combo] step=2", ComboLog.Step(2));
            Assert.Equal("[Combo] step=3", ComboLog.Step(3));
            Assert.Equal("[Combo] heavy", ComboLog.Heavy);
            Assert.Throws<ArgumentOutOfRangeException>(() => ComboLog.Step(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComboLog.Step(4));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QaLog_FullCombo_LogsEachFiredStepOnce(bool twoHanded)
        {
            var m = Machine(twoHanded ? WeaponComboConfig.Default2H() : WeaponComboConfig.Default1H());
            var lines = Logged(m, out _);
            Tap(m); RunUntilPhaseChanges(m);
            Tap(m); RunUntilPhaseChanges(m);
            Tap(m); RunUntilPhaseChanges(m);
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Assert.Equal(new[] { "[Combo] step=1", "[Combo] step=2", "[Combo] step=3" }, lines);
        }

        [Fact]
        public void QaLog_IgnoredTapsMidSwing_LogNothing_BufferedChainLogsWhenItFires()
        {
            var m = Machine();
            var lines = Logged(m, out _);
            Tap(m);
            Run(m, 0.10);
            Assert.Equal(ComboInputResult.Ignored, Tap(m)); // mid-swing spam
            Assert.Equal(ComboInputResult.Ignored, Tap(m));
            Assert.Equal(new[] { "[Combo] step=1" }, lines);
            double left = m.Weapon.SwingRealSeconds(1) - m.SwingElapsed;
            Run(m, left - 0.05);
            Assert.Equal(ComboInputResult.BufferedStep, Tap(m));
            Assert.Equal(new[] { "[Combo] step=1" }, lines); // buffered input alone logs nothing
            Run(m, 0.10);
            Assert.Equal(2, m.Step);
            Assert.Equal(new[] { "[Combo] step=1", "[Combo] step=2" }, lines);
        }

        [Fact]
        public void QaLog_WindowExpired_NextTapLogsStepOneAgain()
        {
            var m = Machine();
            var lines = Logged(m, out _);
            Tap(m); RunUntilPhaseChanges(m);
            RunUntilPhaseChanges(m); // window expires
            Assert.Equal(ComboPhase.Idle, m.Phase);
            Tap(m);
            Assert.Equal(new[] { "[Combo] step=1", "[Combo] step=1" }, lines);
        }

        [Fact]
        public void QaLog_Hold_LogsHeavyOnly()
        {
            var m = Machine();
            var lines = Logged(m, out _);
            m.PressAttack(true);
            Run(m, 0.58);
            Assert.Empty(lines);
            Run(m, 0.04);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Run(m, 1.0);
            Assert.Equal(new[] { "[Combo] heavy" }, lines);
        }

        [Fact]
        public void QaLog_HoldFromWindow_LogsStepThenHeavy()
        {
            var m = Machine();
            var lines = Logged(m, out _);
            Tap(m); RunUntilPhaseChanges(m);
            m.PressAttack(true);
            Run(m, 0.62);
            m.ReleaseAttack(true);
            Assert.Equal(new[] { "[Combo] step=1", "[Combo] heavy" }, lines);
        }

        [Fact]
        public void QaLog_RejectedHeavy_CanCastFalse_LogsNothing()
        {
            var m = Machine();
            var slot = ChargedSkillSlot.WarriorHeavySlash();
            slot.CanCast = id => false;
            m.ChargedSkill = slot;
            var lines = Logged(m, out _);
            m.PressAttack(true);
            Run(m, 0.7);
            Assert.Equal(ComboInputResult.HoldConsumed, m.ReleaseAttack(true));
            Run(m, 1.0);
            Assert.Empty(lines);
        }

        [Fact]
        public void QaLog_Dispose_StopsLogging()
        {
            var m = Machine();
            var lines = Logged(m, out ComboStepLogger logger);
            Tap(m);
            logger.Dispose();
            RunUntilPhaseChanges(m);
            Tap(m);
            Assert.Equal(new[] { "[Combo] step=1" }, lines);
            Assert.Throws<ArgumentNullException>(() => new ComboStepLogger(null, lines.Add));
            Assert.Throws<ArgumentNullException>(() => new ComboStepLogger(m, null));
        }
    }
}
