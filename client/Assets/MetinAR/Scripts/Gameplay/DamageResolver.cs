using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay
{
    public enum HitOutcome
    {
        /// <summary>Geometry missed or the HitChance roll failed: 0 damage.</summary>
        Miss = 0,
        Hit = 1,
        /// <summary>Shield block: −60% damage.</summary>
        Blocked = 2,
        /// <summary>Riposte (timed block): −100%, 0 damage.</summary>
        Parried = 3,
    }

    /// <summary>Input of one hit. All chances/resistances are fractions 0…1.</summary>
    public sealed class DamageRequest
    {
        public int AttackerLevel = 1;
        public int TargetLevel = 1;

        /// <summary>Atk (physical) or Mag (magical) of the attacker.</summary>
        public double Power;
        /// <summary>Skill coefficient (basic attack = 1.0), already scaled by rank.</summary>
        public double Coefficient = 1.0;
        public double Flat;
        public DamageKind Kind = DamageKind.Physical;
        public DamageType Type = DamageType.Physical;

        public double AttackerAccuracy;
        public double TargetEvasion;
        /// <summary>AR/FPP: the blade/projectile geometrically intersected the hit volume. False = miss without a roll.</summary>
        public bool GeometricHit = true;
        /// <summary>No HitChance roll (crowd-control skills, DoT ticks).</summary>
        public bool SkipHitRoll;

        public bool CanCrit = true;
        /// <summary>Attacker CritChance (already capped at 40% by the stat formula).</summary>
        public double CritChance;
        /// <summary>Weak point bonus added to CritChance (e.g. spider eyes +0.15).</summary>
        public double WeakPointCritBonus;
        public double CritMultiplier = 1.6;

        public double TargetDefense;
        /// <summary>Target resistance against <see cref="Type"/> (capped at 50%).</summary>
        public double TargetResistance;

        /// <summary>Target has a shield and the attack comes from the front.</summary>
        public bool TargetCanBlock;
        public double TargetBlockChance;
        /// <summary>Block started inside the riposte window (≤ 0.25 s before the hit frame).</summary>
        public bool TargetParryWindow;

        /// <summary>
        /// Buff/debuff groups (step 8): values inside a group are summed, groups are multiplied:
        /// × Π(1 + Σ group). Example: { +0.10, −0.15 } and { +0.05 vs family } → 0.95 × 1.05.
        /// </summary>
        public List<double[]> ModifierGroups = new List<double[]>();

        /// <summary>Apply the ×U(0.92; 1.08) spread (step 4).</summary>
        public bool ApplyVariance = true;
    }

    public readonly struct DamageResult
    {
        public readonly HitOutcome Outcome;
        public readonly int Damage;
        public readonly bool IsCritical;
        /// <summary>HitChance used for the roll (NaN when no roll was made).</summary>
        public readonly double HitChance;
        public readonly double Mitigation;
        /// <summary>Damage after all steps before rounding and the minimum of 1.</summary>
        public readonly double UnroundedDamage;

        public DamageResult(HitOutcome outcome, int damage, bool isCritical, double hitChance, double mitigation, double unroundedDamage)
        {
            Outcome = outcome; Damage = damage; IsCritical = isCritical; HitChance = hitChance; Mitigation = mitigation; UnroundedDamage = unroundedDamage;
        }

        public bool Landed { get { return Outcome == HitOutcome.Hit || Outcome == HitOutcome.Blocked; } }
    }

    /// <summary>
    /// Resolves one hit in the order of COMBAT_PHYSICS.md §1:
    /// 1 hit/miss → 2 block/riposte → 3 raw → 4 spread → 5 crit → 6 Def mitigation → 7 resistance → 8 modifiers → 9 min 1, round.
    /// Random draws happen in this order and only when needed: HitChance roll, block roll, spread, crit roll.
    /// Pure C#, no Unity dependency.
    /// </summary>
    public sealed class DamageResolver
    {
        private readonly BalanceConfig _config;

        public DamageResolver(BalanceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
        }

        /// <summary>clamp(0.92 + (Acc − Eva) × 0.004 − max(0, L_target − L_attacker) × 0.02, 0.40, 0.99).</summary>
        public double HitChance(double attackerAccuracy, double targetEvasion, int attackerLevel, int targetLevel)
        {
            DamageRules r = _config.Damage;
            double chance = r.HitChanceBase + (attackerAccuracy - targetEvasion) * r.HitChancePerAccuracyPoint
                            - Math.Max(0, targetLevel - attackerLevel) * r.HitChancePenaltyPerTargetLevelAbove;
            return GameplayMath.Clamp(chance, r.HitChanceMin, r.HitChanceMax);
        }

        /// <summary>Def / (Def + 50 + 8 × L_attacker); magical damage uses half Def.</summary>
        public double Mitigation(double targetDefense, int attackerLevel, DamageKind kind)
        {
            DamageRules r = _config.Damage;
            double def = Math.Max(0, targetDefense) * (kind == DamageKind.Magical ? r.MagicalDefenseFactor : 1.0);
            double denominator = def + r.MitigationConstant + r.MitigationPerAttackerLevel * attackerLevel;
            return denominator <= 0 ? 0 : def / denominator;
        }

        /// <summary>Π(1 + Σ group), never below 0.</summary>
        public static double ModifierMultiplier(IList<double[]> groups)
        {
            double m = 1.0;
            if (groups == null) return m;
            foreach (var g in groups)
            {
                if (g == null) continue;
                double sum = 0;
                for (int i = 0; i < g.Length; i++) sum += g[i];
                m *= 1.0 + sum;
            }
            return Math.Max(0, m);
        }

        /// <summary>
        /// Average landed non-critical hit without spread: (Power × coef + flat) × (1 − Mitigation) × (1 − Res) × modifiers,
        /// unrounded. Used by balance sanity checks (tables/sanity_warrior_ttk.csv "avgHitNoCrit").
        /// </summary>
        public double ExpectedHitNoCrit(DamageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            double raw = request.Power * request.Coefficient + request.Flat;
            double mitigation = Mitigation(request.TargetDefense, request.AttackerLevel, request.Kind);
            double res = GameplayMath.Clamp(request.TargetResistance, 0, _config.Stats.ResistanceCap);
            return raw * (1 - mitigation) * (1 - res) * ModifierMultiplier(request.ModifierGroups);
        }

        public DamageResult Resolve(DamageRequest request, IGameplayRandom random)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (random == null) throw new ArgumentNullException(nameof(random));
            DamageRules r = _config.Damage;
            double mitigation = Mitigation(request.TargetDefense, request.AttackerLevel, request.Kind);

            // 1. Hit / miss: geometry first, then the HitChance roll for geometric hits only.
            double hitChance = double.NaN;
            if (!request.GeometricHit) return new DamageResult(HitOutcome.Miss, 0, false, hitChance, mitigation, 0);
            if (!request.SkipHitRoll)
            {
                hitChance = HitChance(request.AttackerAccuracy, request.TargetEvasion, request.AttackerLevel, request.TargetLevel);
                if (random.NextDouble() >= hitChance) return new DamageResult(HitOutcome.Miss, 0, false, hitChance, mitigation, 0);
            }

            // 2. Block / riposte (only shield + frontal attack).
            HitOutcome outcome = HitOutcome.Hit;
            if (request.TargetCanBlock)
            {
                if (request.TargetParryWindow) return new DamageResult(HitOutcome.Parried, 0, false, hitChance, mitigation, 0);
                double block = GameplayMath.Clamp(request.TargetBlockChance, 0, _config.Stats.BlockChanceCap);
                if (block > 0 && random.NextDouble() < block) outcome = HitOutcome.Blocked;
            }

            // 3. Raw damage.
            double damage = request.Power * request.Coefficient + request.Flat;

            // 4. Spread ×U(0.92; 1.08).
            if (request.ApplyVariance) damage *= r.VarianceMin + (r.VarianceMax - r.VarianceMin) * random.NextDouble();

            // 5. Critical hit.
            bool crit = false;
            if (request.CanCrit)
            {
                double critChance = GameplayMath.Clamp01(request.CritChance + request.WeakPointCritBonus);
                if (critChance > 0 && random.NextDouble() < critChance)
                {
                    crit = true;
                    damage *= Math.Min(request.CritMultiplier, _config.Stats.CritMultiplierCap);
                }
            }

            // 6. Defense with diminishing returns.
            damage *= 1 - mitigation;

            // 7. Resistance of the damage type (cap 50%).
            damage *= 1 - GameplayMath.Clamp(request.TargetResistance, 0, _config.Stats.ResistanceCap);

            // 8. Buffs/debuffs and family bonus; block reduction.
            damage *= ModifierMultiplier(request.ModifierGroups);
            if (outcome == HitOutcome.Blocked) damage *= 1 - r.BlockDamageReduction;

            // 9. Minimum 1, rounded to an integer.
            int final = Math.Max(r.MinimumDamage, GameplayMath.RoundToInt(damage));
            return new DamageResult(outcome, final, crit, hitChance, mitigation, damage);
        }
    }
}
