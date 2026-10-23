using System;
using System.Collections.Generic;

namespace MetinAR.ArEncounter
{
    /// <summary>Weapon handling of the warrior combo (METINAR-039, #82).</summary>
    public enum ComboWeaponKind { OneHanded = 1, TwoHanded = 2 }

    /// <summary>
    /// Direction of one combo swing as seen by the player. <see cref="Finisher"/> is cios 3: "z lewej albo z góry"
    /// (issue #82 / MANAGER 03:40); which of the two is decided by the animation clip, not by this logic.
    /// </summary>
    public enum ComboSwingArc { LeftToRight = 1, RightToLeft = 2, Finisher = 3 }

    /// <summary>Hand layout hook for the view: 1H = right hand on the grip, 2H = both hands on the grip.</summary>
    public enum ComboHandLayout { RightHandGrip = 1, BothHandsOnGrip = 2 }

    /// <summary>
    /// One combo step. Times are clip seconds at <see cref="WeaponComboConfig.AttackSpeed"/> = 1.
    /// The hit interval is the only part of the swing in which the blade can register a hit.
    /// </summary>
    public sealed class ComboStepConfig
    {
        public ComboSwingArc Arc;
        public double SwingSeconds;
        public double HitStartSeconds;
        public double HitEndSeconds;
        /// <summary>Per-step multiplier fed to the S1 formulas as <c>DamageRequest.Coefficient</c> (basic attack = 1.0).</summary>
        public double DamageMultiplier = 1.0;

        public ComboStepConfig Clone()
        {
            return new ComboStepConfig
            {
                Arc = Arc, SwingSeconds = SwingSeconds, HitStartSeconds = HitStartSeconds,
                HitEndSeconds = HitEndSeconds, DamageMultiplier = DamageMultiplier,
            };
        }
    }

    /// <summary>
    /// Data for one weapon type of the warrior combo: three steps, combo window, speed, reach, damage multipliers and the
    /// presentation hooks a weapon switch must swap (model, hand layout, animation set, sounds). Engine-free; the Unity
    /// side reads it. Every number in <see cref="Default1H"/>/<see cref="Default2H"/> is a technical prototype value, not
    /// approved balance (same status as the #31 FPP timings it starts from).
    /// </summary>
    public sealed class WeaponComboConfig
    {
        public const int StepCount = 3;

        public ComboWeaponKind Kind;
        /// <summary>Item types that use this config (tables/item_slots.csv, CLASSES_SKILLS.md: warrior weapons).</summary>
        public string[] ItemTypes = new string[0];
        public ComboStepConfig[] Steps = new ComboStepConfig[0];

        /// <summary>Speed factor: real seconds = clip seconds / AttackSpeed. 1.0 = the clip timing above.</summary>
        public double AttackSpeed = 1.0;
        /// <summary>Real seconds after cios 1 or cios 2 ends in which a tap ATAK chains the next step.</summary>
        public double ComboWindowSeconds;
        /// <summary>A tap in the last part of a swing (real seconds) is buffered and chains when the swing ends (COMBAT_PHYSICS.md §4: 0.15 s).</summary>
        public double InputBufferSeconds;
        /// <summary>Holding ATAK at least this long is the charged-skill input instead of a tap (COMBAT_PHYSICS.md §4: 0.6 s).</summary>
        public double HoldThresholdSeconds;

        /// <summary>Blade segment along the grip axis in metres (reach). A hit needs this segment to cross the target body.</summary>
        public double BladeBaseMeters;
        public double BladeTipMeters;

        /// <summary>2H blocks the shield (ITEMS_COMBAT_SPEC.md §1).</summary>
        public bool AllowsShield;
        public ComboHandLayout HandLayout;
        /// <summary>Presentation hooks swapped on weapon switch. Null = not provided yet (asset dependency, see #82 part A).</summary>
        public string ModelId;
        public string AnimationSetId;
        public string SwingSoundId;
        public string HitSoundId;

        /// <summary>"1H" / "2H" as used by the <c>[Combo] … weapon=</c> log line.</summary>
        public string Label { get { return Kind == ComboWeaponKind.TwoHanded ? "2H" : "1H"; } }

        public ComboStepConfig Step(int step)
        {
            if (step < 1 || step > Steps.Length) throw new ArgumentOutOfRangeException(nameof(step), step, "Combo step is 1..3.");
            return Steps[step - 1];
        }

        /// <summary>Real seconds of a step's swing at the configured speed.</summary>
        public double SwingRealSeconds(int step) { return Step(step).SwingSeconds / AttackSpeed; }

        public bool UsesItemType(string itemType)
        {
            if (string.IsNullOrEmpty(itemType) || ItemTypes == null) return false;
            return Array.IndexOf(ItemTypes, itemType) >= 0;
        }

        /// <summary>Returns the list of problems; empty = usable. Invalid configs are rejected by <see cref="WarriorComboMachine"/>.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Kind != ComboWeaponKind.OneHanded && Kind != ComboWeaponKind.TwoHanded) errors.Add("Kind");
            if (Steps == null || Steps.Length != StepCount) errors.Add("Steps must contain exactly 3 steps");
            else
            {
                for (int i = 0; i < Steps.Length; i++)
                {
                    ComboStepConfig s = Steps[i];
                    string p = "Steps[" + i + "].";
                    if (s == null) { errors.Add(p + "null"); continue; }
                    if (!Positive(s.SwingSeconds)) errors.Add(p + "SwingSeconds");
                    if (!Finite(s.HitStartSeconds) || !Finite(s.HitEndSeconds) || s.HitStartSeconds < 0
                        || s.HitEndSeconds <= s.HitStartSeconds || s.HitEndSeconds > s.SwingSeconds) errors.Add(p + "HitWindow");
                    if (!Positive(s.DamageMultiplier)) errors.Add(p + "DamageMultiplier");
                }
                if (errors.Count == 0)
                {
                    if (Steps[0].Arc != ComboSwingArc.LeftToRight) errors.Add("Steps[0].Arc must be LeftToRight (cios 1 L→P)");
                    if (Steps[1].Arc != ComboSwingArc.RightToLeft) errors.Add("Steps[1].Arc must be RightToLeft (cios 2 P→L)");
                    if (Steps[2].Arc != ComboSwingArc.Finisher) errors.Add("Steps[2].Arc must be Finisher (cios 3)");
                }
            }
            if (!Positive(AttackSpeed)) errors.Add("AttackSpeed");
            if (!Positive(ComboWindowSeconds)) errors.Add("ComboWindowSeconds");
            if (!Finite(InputBufferSeconds) || InputBufferSeconds < 0) errors.Add("InputBufferSeconds");
            if (!Positive(HoldThresholdSeconds)) errors.Add("HoldThresholdSeconds");
            else if (Finite(InputBufferSeconds) && InputBufferSeconds >= HoldThresholdSeconds)
                errors.Add("InputBufferSeconds must be shorter than HoldThresholdSeconds");
            if (!Finite(BladeBaseMeters) || BladeBaseMeters < 0 || !Finite(BladeTipMeters) || BladeTipMeters <= BladeBaseMeters)
                errors.Add("Blade");
            if (Kind == ComboWeaponKind.TwoHanded && AllowsShield) errors.Add("2H must not allow a shield");
            if (Kind == ComboWeaponKind.TwoHanded && HandLayout != ComboHandLayout.BothHandsOnGrip) errors.Add("2H needs both hands on the grip");
            if (Kind == ComboWeaponKind.OneHanded && HandLayout != ComboHandLayout.RightHandGrip) errors.Add("1H swings with the right hand");
            return errors;
        }

        public WeaponComboConfig Clone()
        {
            var steps = new ComboStepConfig[Steps == null ? 0 : Steps.Length];
            for (int i = 0; i < steps.Length; i++) steps[i] = Steps[i] == null ? null : Steps[i].Clone();
            return new WeaponComboConfig
            {
                Kind = Kind, ItemTypes = ItemTypes == null ? new string[0] : (string[])ItemTypes.Clone(), Steps = steps,
                AttackSpeed = AttackSpeed, ComboWindowSeconds = ComboWindowSeconds, InputBufferSeconds = InputBufferSeconds,
                HoldThresholdSeconds = HoldThresholdSeconds, BladeBaseMeters = BladeBaseMeters, BladeTipMeters = BladeTipMeters,
                AllowsShield = AllowsShield, HandLayout = HandLayout, ModelId = ModelId, AnimationSetId = AnimationSetId,
                SwingSoundId = SwingSoundId, HitSoundId = HitSoundId,
            };
        }

        /// <summary>
        /// 1H sword (cios 1 L→P, cios 2 P→L, cios 3 finisher). Timing starts from the #31 swing (0.72 s, hit 0.18–0.42 s)
        /// and the current blade reach (FppViewFraming: 0.13–0.94 m × 0.75). Window, finisher multiplier: PROTOTYPE values.
        /// </summary>
        public static WeaponComboConfig Default1H()
        {
            return new WeaponComboConfig
            {
                Kind = ComboWeaponKind.OneHanded,
                ItemTypes = new[] { "sword_1h" },
                Steps = new[]
                {
                    new ComboStepConfig { Arc = ComboSwingArc.LeftToRight, SwingSeconds = 0.72, HitStartSeconds = 0.18, HitEndSeconds = 0.42, DamageMultiplier = 1.0 },
                    new ComboStepConfig { Arc = ComboSwingArc.RightToLeft, SwingSeconds = 0.72, HitStartSeconds = 0.18, HitEndSeconds = 0.42, DamageMultiplier = 1.0 },
                    new ComboStepConfig { Arc = ComboSwingArc.Finisher, SwingSeconds = 0.80, HitStartSeconds = 0.22, HitEndSeconds = 0.48, DamageMultiplier = 1.3 },
                },
                AttackSpeed = 1.0,
                ComboWindowSeconds = 0.35,
                InputBufferSeconds = 0.15,
                HoldThresholdSeconds = 0.6,
                BladeBaseMeters = 0.13 * 0.75,
                BladeTipMeters = 0.94 * 0.75,
                AllowsShield = true,
                HandLayout = ComboHandLayout.RightHandGrip,
                ModelId = "starter_sword",
                AnimationSetId = "warrior_combo_1h",
                SwingSoundId = "SFX_Combat_SwordSwing_01",
                HitSoundId = "SFX_Combat_SwordHit_01",
            };
        }

        /// <summary>
        /// 2H sword/axe: same L→P, P→L, finisher rhythm; slower, stronger per hit, longer reach, no shield, both hands.
        /// All numbers are PROTOTYPE values. No 2H model or 2H sounds exist yet, so those hooks are null until assets land.
        /// </summary>
        public static WeaponComboConfig Default2H()
        {
            return new WeaponComboConfig
            {
                Kind = ComboWeaponKind.TwoHanded,
                ItemTypes = new[] { "sword_2h", "axe_2h" },
                Steps = new[]
                {
                    new ComboStepConfig { Arc = ComboSwingArc.LeftToRight, SwingSeconds = 1.00, HitStartSeconds = 0.32, HitEndSeconds = 0.62, DamageMultiplier = 1.4 },
                    new ComboStepConfig { Arc = ComboSwingArc.RightToLeft, SwingSeconds = 1.00, HitStartSeconds = 0.32, HitEndSeconds = 0.62, DamageMultiplier = 1.4 },
                    new ComboStepConfig { Arc = ComboSwingArc.Finisher, SwingSeconds = 1.15, HitStartSeconds = 0.38, HitEndSeconds = 0.72, DamageMultiplier = 1.8 },
                },
                AttackSpeed = 1.0,
                ComboWindowSeconds = 0.45,
                InputBufferSeconds = 0.15,
                HoldThresholdSeconds = 0.6,
                BladeBaseMeters = 0.13 * 0.75,
                BladeTipMeters = 1.20 * 0.75,
                AllowsShield = false,
                HandLayout = ComboHandLayout.BothHandsOnGrip,
                ModelId = null,
                AnimationSetId = "warrior_combo_2h",
                SwingSoundId = null,
                HitSoundId = null,
            };
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        private static bool Positive(double v) { return Finite(v) && v > 0; }
    }

    /// <summary>
    /// Blade reach against a target body box. The caller supplies the real grip pose of the current animation frame in the
    /// target's local frame (origin + unit direction along the blade); the blade segment is
    /// [<see cref="WeaponComboConfig.BladeBaseMeters"/>, <see cref="WeaponComboConfig.BladeTipMeters"/>] along it.
    /// </summary>
    public static class ComboBladeProbe
    {
        public static bool Intersects(WeaponComboConfig weapon, FppPoint gripOrigin, FppPoint bladeDirection, FppPoint boxMin, FppPoint boxMax)
        {
            if (weapon == null || !gripOrigin.IsFinite || !bladeDirection.IsFinite) return false;
            double len = Math.Sqrt(bladeDirection.X * bladeDirection.X + bladeDirection.Y * bladeDirection.Y + bladeDirection.Z * bladeDirection.Z);
            if (len < 1e-9) return false;
            double ux = bladeDirection.X / len, uy = bladeDirection.Y / len, uz = bladeDirection.Z / len;
            var a = new FppPoint(gripOrigin.X + ux * weapon.BladeBaseMeters, gripOrigin.Y + uy * weapon.BladeBaseMeters, gripOrigin.Z + uz * weapon.BladeBaseMeters);
            var b = new FppPoint(gripOrigin.X + ux * weapon.BladeTipMeters, gripOrigin.Y + uy * weapon.BladeTipMeters, gripOrigin.Z + uz * weapon.BladeTipMeters);
            return FppHitGeometry.IntersectsBox(a, b, boxMin, boxMax);
        }
    }
}
