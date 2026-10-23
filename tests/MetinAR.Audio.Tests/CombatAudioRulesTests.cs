using MetinAR.Audio;
using Xunit;

namespace MetinAR.Audio.Tests
{
    public class CombatAudioRulesTests
    {
        private static List<GameAudioEvent> Hit(bool resolved = true, bool landed = true, bool blocked = false,
            bool crit = false, bool killed = false, int levels = 0)
        {
            var list = new List<GameAudioEvent>();
            CombatAudioRules.ForHit(resolved, landed, blocked, crit, killed, levels, list);
            return list;
        }

        [Fact] public void NormalHit() => Assert.Equal(new[] { GameAudioEvent.SwordHit, GameAudioEvent.MobHurt }, Hit());
        [Fact] public void CriticalHit() => Assert.Equal(new[] { GameAudioEvent.SwordCrit, GameAudioEvent.MobHurt }, Hit(crit: true));
        [Fact] public void BlockedHit() => Assert.Equal(new[] { GameAudioEvent.ShieldBlock, GameAudioEvent.MobHurt }, Hit(blocked: true));
        [Fact] public void Miss() => Assert.Equal(new[] { GameAudioEvent.SwordMiss }, Hit(landed: false));
        [Fact] public void ParryIsShieldNotWhiff() => Assert.Equal(new[] { GameAudioEvent.ShieldBlock }, Hit(landed: false, blocked: true));
        [Fact] public void DuplicateSwingIsSilent() => Assert.Empty(Hit(resolved: false));

        [Fact]
        public void KillGivesDeathAndCoins()
        {
            Assert.Equal(new[] { GameAudioEvent.SwordHit, GameAudioEvent.MobDeath, GameAudioEvent.RewardCoins }, Hit(killed: true));
        }

        [Fact]
        public void KillWithLevelUpAddsStinger()
        {
            Assert.Equal(new[] { GameAudioEvent.SwordCrit, GameAudioEvent.MobDeath, GameAudioEvent.RewardCoins, GameAudioEvent.LevelUp },
                Hit(crit: true, killed: true, levels: 1));
        }

        [Fact]
        public void FppObserverEmitsSwingOncePerSwingId()
        {
            var o = new FppAudioObserver();
            var ev = new List<GameAudioEvent>();
            Assert.Null(o.Observe(0, false, false, ev));
            Assert.Empty(ev);
            o.Observe(1, false, false, ev);
            o.Observe(1, false, false, ev);
            o.Observe(1, false, false, ev);
            o.Observe(2, false, false, ev);
            Assert.Equal(new[] { GameAudioEvent.SwordSwing, GameAudioEvent.SwordSwing }, ev);
        }

        [Fact]
        public void FppObserverAuraLifecycle()
        {
            var o = new FppAudioObserver();
            var ev = new List<GameAudioEvent>();
            o.Observe(0, false, false, ev);
            Assert.Null(o.Observe(0, true, false, ev));          // cast starts
            Assert.Equal(new[] { GameAudioEvent.AuraStart }, ev);
            Assert.True(o.Observe(0, true, true, ev));           // burst → aura active → loop on
            Assert.Null(o.Observe(0, false, true, ev));          // still active, no repeat
            Assert.True(o.LoopPlaying);
            Assert.False(o.Observe(0, false, false, ev));        // expired → stop + loop off
            Assert.Equal(new[] { GameAudioEvent.AuraStart, GameAudioEvent.AuraStop }, ev);
            Assert.False(o.LoopPlaying);
        }

        [Fact]
        public void FppObserverFirstFrameDoesNotReplayPastSwing()
        {
            var o = new FppAudioObserver();
            var ev = new List<GameAudioEvent>();
            o.Observe(7, false, false, ev);
            Assert.Empty(ev);
        }
    }
}
