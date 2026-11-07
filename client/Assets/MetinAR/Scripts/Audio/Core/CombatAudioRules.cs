using System.Collections.Generic;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): which sounds a resolved hit makes (COMBAT_PHYSICS.md §5). Inputs are primitives so the rule
    /// is testable without the gameplay assembly; the bridge feeds it from <c>CombatHitReport</c>.
    /// </summary>
    public static class CombatAudioRules
    {
        /// <param name="blocked">The mob's shield took the hit (HitOutcome.Blocked −60 % or Parried −100 %): shield clank
        /// instead of flesh hit. A parry is not "landed" (0 damage) but still sounds as a block, not as a whiff.</param>
        public static void ForHit(bool resolved, bool landed, bool blocked, bool critical, bool killed, int levelsGained,
            List<GameAudioEvent> into)
        {
            if (!resolved) return; // duplicate swing / mob already dead: the swing itself already sounded.
            if (!landed && blocked)
            {
                into.Add(GameAudioEvent.ShieldBlock);
                return;
            }

            if (!landed)
            {
                into.Add(GameAudioEvent.SwordMiss);
                return;
            }

            into.Add(blocked ? GameAudioEvent.ShieldBlock : critical ? GameAudioEvent.SwordCrit : GameAudioEvent.SwordHit);
            if (killed)
            {
                into.Add(GameAudioEvent.MobDeath);
                into.Add(GameAudioEvent.RewardCoins);
            }
            else
            {
                into.Add(GameAudioEvent.MobHurt);
            }

            if (levelsGained > 0) into.Add(GameAudioEvent.LevelUp);
        }
    }

    /// <summary>
    /// Edge detector over the FPP combat timeline (polled each frame): a new swing id → swing whoosh; Aura cast start →
    /// AuraStart; Aura active → loop on; Aura end → AuraStop + loop off. Needs no edits in the combat controller.
    /// </summary>
    public sealed class FppAudioObserver
    {
        private bool _initialised;
        private int _swingId;
        private bool _casting;
        private bool _auraActive;

        public bool LoopPlaying => _auraActive;

        public void Reset()
        {
            _initialised = false;
            _swingId = 0;
            _casting = false;
            _auraActive = false;
        }

        /// <returns>null = loop unchanged, true = start Aura loop, false = stop Aura loop.</returns>
        public bool? Observe(int swingId, bool auraCasting, bool auraActive, List<GameAudioEvent> into)
        {
            if (!_initialised)
            {
                _initialised = true;
                _swingId = swingId;
                _casting = auraCasting;
                _auraActive = auraActive;
                return auraActive ? true : (bool?)null;
            }

            if (swingId != _swingId)
            {
                _swingId = swingId;
                into.Add(GameAudioEvent.SwordSwing);
            }

            if (auraCasting && !_casting) into.Add(GameAudioEvent.AuraStart);
            _casting = auraCasting;

            bool? loop = null;
            if (auraActive && !_auraActive) loop = true;
            else if (!auraActive && _auraActive)
            {
                into.Add(GameAudioEvent.AuraStop);
                loop = false;
            }

            _auraActive = auraActive;
            return loop;
        }
    }
}
