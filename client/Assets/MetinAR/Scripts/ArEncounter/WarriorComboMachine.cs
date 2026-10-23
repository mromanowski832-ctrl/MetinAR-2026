using System;
using System.Collections.Generic;

namespace MetinAR.ArEncounter
{
    public enum ComboPhase
    {
        /// <summary>No swing; the next tap starts cios 1.</summary>
        Idle = 0,
        /// <summary>Cios <see cref="WarriorComboMachine.Step"/> is playing.</summary>
        Swing = 1,
        /// <summary>Cios 1 or 2 finished; a tap within the combo window chains the next step.</summary>
        Window = 2,
    }

    /// <summary>What a release of ATAK did.</summary>
    public enum ComboInputResult
    {
        None = 0,
        /// <summary>Tap started a swing now (cios 1, or the next step from the combo window).</summary>
        StartedStep = 1,
        /// <summary>Tap in the last part of cios 1/2: the next step starts when this swing ends.</summary>
        BufferedStep = 2,
        /// <summary>Tap outside every accepting phase (anti-spam): nothing happens.</summary>
        Ignored = 3,
        /// <summary>The press reached the hold threshold and was resolved in <see cref="WarriorComboMachine.Tick"/>; release does nothing.</summary>
        HoldConsumed = 4,
    }

    /// <summary>Charged skill bound to "hold ATAK" (warrior: <c>warrior_heavy_slash</c>, CLASSES_SKILLS.md).</summary>
    public sealed class ChargedSkillSlot
    {
        public string SkillId;
        public bool Unlocked;
        public bool Assigned;
        public bool Available { get { return Unlocked && Assigned && !string.IsNullOrEmpty(SkillId); } }
    }

    /// <summary>A hit registered by blade sampling (never by the button). Feed it to <c>EncounterCombatSession.RegisterHit</c>.</summary>
    public readonly struct ComboHit
    {
        public readonly int TargetId;
        public readonly int SwingId;
        public readonly int Step;
        public readonly ComboWeaponKind Weapon;
        public readonly double DamageMultiplier;
        /// <summary>Clip time of the first blade sample that crossed the body.</summary>
        public readonly double ClipTime;

        public ComboHit(int targetId, int swingId, int step, ComboWeaponKind weapon, double damageMultiplier, double clipTime)
        {
            TargetId = targetId; SwingId = swingId; Step = step; Weapon = weapon; DamageMultiplier = damageMultiplier; ClipTime = clipTime;
        }
    }

    /// <summary>Log line required by the #82 Unity smoke: <c>[Combo] step=1/2/3 weapon=1H/2H hit=…</c>.</summary>
    public static class ComboLog
    {
        public const string Prefix = "[Combo] ";

        public static string Line(int step, ComboWeaponKind weapon, bool hit, int swingId)
        {
            return Prefix + "step=" + step + " weapon=" + (weapon == ComboWeaponKind.TwoHanded ? "2H" : "1H")
                + " hit=" + (hit ? "yes" : "no") + " swing=" + swingId;
        }
    }

    /// <summary>
    /// METINAR-039 (#82) warrior basic-attack combo, engine-free. Three steps per weapon (cios 1 L→P, cios 2 P→L, cios 3
    /// finisher) with separate 1H/2H <see cref="WeaponComboConfig"/>.
    /// <list type="bullet">
    /// <item>ATAK is decided on release: released before <see cref="WeaponComboConfig.HoldThresholdSeconds"/> = tap.</item>
    /// <item>Tap in Idle → cios 1. Tap pressed inside the combo window after cios 1/2 → next step. Tap pressed in the last
    ///   <see cref="WeaponComboConfig.InputBufferSeconds"/> of cios 1/2 → buffered, chains when the swing ends (COMBAT_PHYSICS.md §4).
    ///   Any other press (mid-swing, during cios 3) is ignored, including as a hold.</item>
    /// <item>No tap inside the window → back to cios 1 (Idle). After cios 3 the combo ends (Idle; next tap = cios 1).</item>
    /// <item>Hold ≥ threshold fires the charged skill only when it is unlocked and assigned, and resets the combo to cios 1.
    ///   A hold never starts or chains a combo swing, so it can never produce a combo hit.</item>
    /// <item>Hits come only from <see cref="SampleBlade"/> (real blade pose vs target) over the step's hit interval crossed by
    ///   the last <see cref="Tick"/>; max 1 hit per target per swing; every step is its own swing (new <see cref="SwingId"/>).</item>
    /// </list>
    /// Invalid/stalled frames and not-ready cancel everything (fail closed), like <see cref="FppCombatTimeline"/>.
    /// Aura coordination is left to the Unity integration (open question on #82).
    /// </summary>
    public sealed class WarriorComboMachine
    {
        public const double MaxStepSeconds = 0.25;
        /// <summary>Blade sampling step (COMBAT_PHYSICS.md §2: every ≤ 10 ms).</summary>
        public const double MaxSampleSeconds = 0.01;

        private WeaponComboConfig _weapon;
        private HashSet<int> _currentHits = new HashSet<int>();
        private HashSet<int> _previousHits = new HashSet<int>();
        private bool _pressed;
        private bool _holdResolved;
        private double _holdSeconds;
        /// <summary>Step the current press starts/chains on release; 0 = ignored press.</summary>
        private int _pressTargetStep;
        private bool _pressBuffered;
        private int _bufferedStep;
        private int _hitSwingId;
        private int _hitStep;

        public WarriorComboMachine(WeaponComboConfig weapon)
        {
            SetWeapon(weapon);
        }

        public WeaponComboConfig Weapon { get { return _weapon; } }
        public ComboPhase Phase { get; private set; }
        /// <summary>1..3 while swinging or in the window after that step; 0 in Idle (next tap = cios 1).</summary>
        public int Step { get; private set; }
        /// <summary>Increments for every started swing (each combo step is a separate swing).</summary>
        public int SwingId { get; private set; }
        /// <summary>Real seconds since the current swing started.</summary>
        public double SwingElapsed { get; private set; }
        /// <summary>Clip time (real × AttackSpeed) of the current swing, for the animation/pose.</summary>
        public double ClipTime { get { return SwingElapsed * _weapon.AttackSpeed; } }
        public double WindowRemaining { get; private set; }
        public bool AttackHeld { get { return _pressed; } }
        public ChargedSkillSlot ChargedSkill { get; set; }

        /// <summary>Clip-time interval crossed by the last <see cref="Tick"/> inside the hit interval of swing <see cref="HitSwingId"/>.</summary>
        public bool HasHitWindow { get; private set; }
        public double HitWindowStart { get; private set; }
        public double HitWindowEnd { get; private set; }
        /// <summary>Swing/step the current hit interval belongs to (it may have ended in the same tick).</summary>
        public int HitSwingId { get { return _hitSwingId; } }
        public int HitStep { get { return _hitStep; } }

        /// <summary>A swing started: (step, swingId).</summary>
        public event Action<int, int> StepStarted;
        /// <summary>The combo went back to cios 1 before cios 3 finished (window expired, cancel, charged skill, weapon switch).</summary>
        public event Action ComboReset;
        /// <summary>Cios 3 finished; the combo ended.</summary>
        public event Action ComboCompleted;
        /// <summary>Hold reached the threshold and the charged skill is unlocked and assigned: skill id.</summary>
        public event Action<string> ChargedSkillFired;
        /// <summary>Hold reached the threshold but the charged skill is locked or not assigned: nothing fires.</summary>
        public event Action ChargedSkillUnavailable;
        /// <summary>Weapon switched: the view swaps model, hand layout, animation set, reach, damage and sounds from this config.</summary>
        public event Action<WeaponComboConfig> WeaponChanged;

        /// <summary>Switches the weapon config (validated, copied). Cancels the running combo; next tap = cios 1 of the new weapon.</summary>
        public void SetWeapon(WeaponComboConfig weapon)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            List<string> errors = weapon.Validate();
            if (errors.Count > 0) throw new ArgumentException("Invalid combo config: " + string.Join("; ", errors.ToArray()), nameof(weapon));
            bool switching = _weapon != null;
            if (switching) Cancel();
            _weapon = weapon.Clone();
            if (switching) WeaponChanged?.Invoke(_weapon);
        }

        /// <summary>ATAK pressed. Nothing starts yet: tap vs hold is known only on release or at the hold threshold.</summary>
        public void PressAttack(bool ready)
        {
            if (!ready || _pressed) return;
            _pressed = true;
            _holdResolved = false;
            _holdSeconds = 0;
            _pressBuffered = false;
            _pressTargetStep = 0;
            if (Phase == ComboPhase.Idle) _pressTargetStep = 1;
            else if (Phase == ComboPhase.Window) _pressTargetStep = Step + 1;
            else if (Phase == ComboPhase.Swing && Step < WeaponComboConfig.StepCount && _bufferedStep == 0
                     && _weapon.SwingRealSeconds(Step) - SwingElapsed <= _weapon.InputBufferSeconds + 1e-9)
            {
                _pressTargetStep = Step + 1;
                _pressBuffered = true;
            }
        }

        /// <summary>ATAK released. Shorter than the hold threshold = tap.</summary>
        public ComboInputResult ReleaseAttack(bool ready)
        {
            if (!_pressed) return ComboInputResult.None;
            _pressed = false;
            if (_holdResolved) return ComboInputResult.HoldConsumed;
            int target = _pressTargetStep;
            bool buffered = _pressBuffered;
            _pressTargetStep = 0;
            _pressBuffered = false;
            if (!ready || target == 0) return ComboInputResult.Ignored;
            if (Phase == ComboPhase.Swing)
            {
                if (!buffered || Step != target - 1) return ComboInputResult.Ignored;
                _bufferedStep = target;
                return ComboInputResult.BufferedStep;
            }
            // Buffered press released after its swing already ended: the window opened at Step = target - 1.
            if (Phase == ComboPhase.Window && Step != target - 1) return ComboInputResult.Ignored;
            if (Phase == ComboPhase.Idle && target != 1) return ComboInputResult.Ignored;
            StartStep(target);
            return ComboInputResult.StartedStep;
        }

        /// <summary>Advances time. Invalid, negative or stalled (&gt; 0.25 s) frames and not-ready cancel everything (fail closed).</summary>
        public void Tick(double deltaSeconds, bool ready)
        {
            HasHitWindow = false;
            if (!ready || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds)
                || deltaSeconds < 0 || deltaSeconds > MaxStepSeconds)
            {
                Cancel();
                return;
            }
            if (deltaSeconds == 0) return;

            if (Phase == ComboPhase.Swing)
            {
                double speed = _weapon.AttackSpeed;
                ComboStepConfig step = _weapon.Step(Step);
                double previousClip = SwingElapsed * speed;
                SwingElapsed += deltaSeconds;
                double clip = SwingElapsed * speed;
                HitWindowStart = Math.Max(previousClip, step.HitStartSeconds);
                HitWindowEnd = Math.Min(clip, step.HitEndSeconds);
                HasHitWindow = clip >= step.HitStartSeconds && previousClip < step.HitEndSeconds && HitWindowEnd >= HitWindowStart;
                _hitSwingId = SwingId;
                _hitStep = Step;
                if (clip >= step.SwingSeconds) EndSwing();
            }
            else if (Phase == ComboPhase.Window)
            {
                // A press that began inside the window keeps it open until it resolves (tap on release or hold at threshold).
                bool pendingPress = _pressed && !_holdResolved && _pressTargetStep > 0;
                if (!pendingPress)
                {
                    WindowRemaining -= deltaSeconds;
                    if (WindowRemaining <= 0)
                    {
                        ResetToIdle();
                        ComboReset?.Invoke();
                    }
                }
            }

            if (_pressed && !_holdResolved)
            {
                _holdSeconds += deltaSeconds;
                if (_holdSeconds >= _weapon.HoldThresholdSeconds) ResolveHold();
            }
        }

        /// <summary>
        /// Samples the real blade over the hit interval crossed by the last <see cref="Tick"/> in ≤ 10 ms steps.
        /// <paramref name="bladeIntersectsAtClipTime"/> must evaluate the same pose as the render at that clip time against the
        /// target body (e.g. via <see cref="ComboBladeProbe.Intersects"/>). Returns true once per target per swing.
        /// </summary>
        public bool SampleBlade(int targetId, Func<double, bool> bladeIntersectsAtClipTime, out ComboHit hit)
        {
            hit = default(ComboHit);
            if (bladeIntersectsAtClipTime == null || !HasHitWindow || _hitSwingId == 0) return false;
            HashSet<int> hits = _hitSwingId == SwingId ? _currentHits : _previousHits;
            if (hits.Contains(targetId)) return false;
            int samples = Math.Max(1, (int)Math.Ceiling((HitWindowEnd - HitWindowStart) / MaxSampleSeconds - 1e-9));
            for (int i = 0; i <= samples; i++)
            {
                double t = HitWindowStart + (HitWindowEnd - HitWindowStart) * i / samples;
                if (!bladeIntersectsAtClipTime(t)) continue;
                hits.Add(targetId);
                hit = new ComboHit(targetId, _hitSwingId, _hitStep, _weapon.Kind, _weapon.Step(_hitStep).DamageMultiplier, t);
                return true;
            }
            return false;
        }

        /// <summary>Fail-closed cancel (tracking loss, pause, weapon switch): back to cios 1, no pending input, no hit interval.</summary>
        public void Cancel()
        {
            bool wasActive = Phase != ComboPhase.Idle;
            _pressed = false;
            _holdResolved = false;
            _holdSeconds = 0;
            _pressTargetStep = 0;
            _pressBuffered = false;
            ResetToIdle();
            HasHitWindow = false;
            HitWindowStart = HitWindowEnd = 0;
            if (wasActive) ComboReset?.Invoke();
        }

        private void ResolveHold()
        {
            _holdResolved = true;
            bool accepted = _pressTargetStep > 0;
            _pressTargetStep = 0;
            _pressBuffered = false;
            if (!accepted) return; // press ignored (mid-swing / cios 3): no tap, no skill.
            ChargedSkillSlot slot = ChargedSkill;
            if (slot != null && slot.Available)
            {
                // Only reachable from Idle/Window (a buffered press resolves after its swing ended, buffer < threshold).
                bool wasActive = Phase != ComboPhase.Idle;
                ResetToIdle();
                if (wasActive) ComboReset?.Invoke();
                ChargedSkillFired?.Invoke(slot.SkillId);
            }
            else
            {
                ChargedSkillUnavailable?.Invoke();
            }
        }

        private void StartStep(int step)
        {
            Phase = ComboPhase.Swing;
            Step = step;
            SwingElapsed = 0;
            WindowRemaining = 0;
            _bufferedStep = 0;
            HashSet<int> recycled = _previousHits;
            _previousHits = _currentHits;
            _currentHits = recycled;
            _currentHits.Clear();
            SwingId++;
            StepStarted?.Invoke(step, SwingId);
        }

        private void EndSwing()
        {
            int finished = Step;
            if (finished >= WeaponComboConfig.StepCount)
            {
                ResetToIdle();
                ComboCompleted?.Invoke();
                return;
            }
            if (_bufferedStep == finished + 1)
            {
                StartStep(_bufferedStep);
                return;
            }
            Phase = ComboPhase.Window;
            WindowRemaining = _weapon.ComboWindowSeconds;
            SwingElapsed = 0;
        }

        private void ResetToIdle()
        {
            Phase = ComboPhase.Idle;
            Step = 0;
            SwingElapsed = 0;
            WindowRemaining = 0;
            _bufferedStep = 0;
        }
    }
}
