using System;
using System.Collections.Generic;

namespace MetinAR.ArEncounter
{
    public enum FppCombatAction { Idle, Attack, Aura }

    /// <summary>Button-driven prototype. All durations are technical defaults, not final balance.</summary>
    public sealed class FppCombatTimeline
    {
        public const float AttackSeconds = 0.72f;
        public const float HitStartSeconds = 0.18f;
        public const float HitEndSeconds = 0.42f;
        public const float AuraCastSeconds = 1.15f;
        public const float AuraBurstSeconds = 0.65f;
        public const float AuraDurationSeconds = 6f;
        public const float MaxStepSeconds = 0.25f;

        private readonly HashSet<int> _hitTargets = new HashSet<int>();
        public FppCombatAction Action { get; private set; }
        public float Elapsed { get; private set; }
        public float AuraRemaining { get; private set; }
        public bool AuraActive => AuraRemaining > 0f;
        public bool HasHitWindow { get; private set; }
        public float HitWindowStart { get; private set; }
        public float HitWindowEnd { get; private set; }
        public int SwingId { get; private set; }

        public bool TryAttack(bool ready)
        {
            if (!ready || Action != FppCombatAction.Idle) return false;
            Action = FppCombatAction.Attack;
            Elapsed = 0f;
            HasHitWindow = false;
            _hitTargets.Clear();
            SwingId++;
            return true;
        }

        public bool TryAura(bool ready)
        {
            if (!ready || Action != FppCombatAction.Idle || AuraActive) return false;
            Action = FppCombatAction.Aura;
            Elapsed = 0f;
            HasHitWindow = false;
            return true;
        }

        /// <summary>
        /// Returns the crossed attack interval for geometry sampling, including frames that cross a boundary.
        /// A long stall/invalid delta fails closed: do not replay a stale attack after resume/tracking loss.
        /// </summary>
        public void Tick(float deltaSeconds, bool ready)
        {
            HasHitWindow = false;
            if (!ready || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)
                || deltaSeconds < 0f || deltaSeconds > MaxStepSeconds)
            {
                Cancel();
                return;
            }

            if (deltaSeconds == 0f) return;
            AuraRemaining = Math.Max(0f, AuraRemaining - deltaSeconds);
            if (Action == FppCombatAction.Idle) return;

            float previous = Elapsed;
            Elapsed += deltaSeconds;
            if (Action == FppCombatAction.Attack)
            {
                HitWindowStart = Math.Max(previous, HitStartSeconds);
                HitWindowEnd = Math.Min(Elapsed, HitEndSeconds);
                HasHitWindow = Elapsed >= HitStartSeconds && previous < HitEndSeconds
                    && HitWindowEnd >= HitWindowStart;
                if (Elapsed >= AttackSeconds) Finish();
            }
            else
            {
                if (previous < AuraBurstSeconds && Elapsed >= AuraBurstSeconds)
                    AuraRemaining = Math.Max(0f, AuraDurationSeconds - (Elapsed - AuraBurstSeconds));
                if (Elapsed >= AuraCastSeconds) Finish();
            }
        }

        public bool TryRecordHit(int targetId)
        {
            return HasHitWindow && _hitTargets.Add(targetId);
        }

        private void Finish()
        {
            Action = FppCombatAction.Idle;
            Elapsed = 0f;
        }

        public void Cancel()
        {
            Finish();
            AuraRemaining = 0f;
            HasHitWindow = false;
            HitWindowStart = HitWindowEnd = 0f;
            _hitTargets.Clear();
        }
    }
}
