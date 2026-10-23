using System;
using System.Collections.Generic;

namespace MetinAR.Gameplay.Combat
{
    public enum CombatHitStatus
    {
        /// <summary>The hit was resolved by <see cref="DamageResolver"/> (it may still be a miss).</summary>
        Resolved = 0,
        /// <summary>This swing already hit the mob: 1 hit per target per swing (COMBAT_PHYSICS.md §2).</summary>
        DuplicateSwing = 1,
        /// <summary>The mob is already defeated; no damage, no second reward.</summary>
        MobAlreadyDefeated = 2,
    }

    /// <summary>Outcome of one <c>HitDetected</c> event fed into <see cref="EncounterCombatSession"/>.</summary>
    public sealed class CombatHitReport
    {
        public CombatHitStatus Status;
        public int SwingId;
        public bool WeakPoint;
        public DamageResult Damage;
        public int HpBefore;
        public int HpAfter;
        public int MaxHp;
        /// <summary>This hit brought the mob to 0 HP.</summary>
        public bool Killed;
        /// <summary>Set only on the killing hit.</summary>
        public MobKillReward? Reward;
        public int PlayerLevelBefore;
        public int PlayerLevelAfter;

        public int LevelsGained { get { return PlayerLevelAfter - PlayerLevelBefore; } }
    }

    /// <summary>
    /// One AR encounter fight (METINAR-020 / S2): an FPP <c>HitDetected(mob, swingId)</c> event →
    /// <see cref="DamageResolver"/> (COMBAT_PHYSICS.md §1 order) → mob HP → death at 0 HP → EXP and talary added to the
    /// local <see cref="PlayerProgress"/> exactly once. The attacker's stats are re-read on every hit, so a level-up takes
    /// effect immediately. Pure C#, deterministic for a given <see cref="IGameplayRandom"/> seed.
    /// Mob attacks and player HP are S3; loot drops are S4.
    /// </summary>
    public sealed class EncounterCombatSession
    {
        private readonly BalanceConfig _config;
        private readonly DamageResolver _resolver;
        private readonly PlayerProgress _player;
        private readonly Func<int, DerivedStats> _attackerStatsForLevel;
        private readonly IGameplayRandom _random;
        private readonly HashSet<int> _resolvedSwings = new HashSet<int>();

        public EncounterCombatSession(BalanceConfig config, MobCombatProfile mob, PlayerProgress player,
            Func<int, DerivedStats> attackerStatsForLevel, IGameplayRandom random, int? startHp = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (mob == null) throw new ArgumentNullException(nameof(mob));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (attackerStatsForLevel == null) throw new ArgumentNullException(nameof(attackerStatsForLevel));
            if (random == null) throw new ArgumentNullException(nameof(random));
            int hp = startHp ?? mob.MaxHp;
            if (hp < 1 || hp > mob.MaxHp) throw new ArgumentOutOfRangeException(nameof(startHp), hp, "Start HP must be 1..MaxHp.");
            _config = config;
            _resolver = new DamageResolver(config);
            Mob = mob;
            _player = player;
            _attackerStatsForLevel = attackerStatsForLevel;
            _random = random;
            MobHp = hp;
        }

        /// <summary>Warrior (or other class) with the starter sword, see <see cref="StarterLoadout"/>.</summary>
        public static EncounterCombatSession WithStarterLoadout(BalanceConfig config, MobCombatProfile mob, PlayerProgress player,
            IGameplayRandom random, int? startHp = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            ClassId classId = player.Class;
            return new EncounterCombatSession(config, mob, player, level => StarterLoadout.Stats(config, classId, level), random, startHp);
        }

        public MobCombatProfile Mob { get; }
        public PlayerProgress Player { get { return _player; } }
        public int MobHp { get; private set; }
        public int MobMaxHp { get { return Mob.MaxHp; } }
        public bool MobDefeated { get { return MobHp <= 0; } }
        public float MobHpFraction { get { return (float)MobHp / Mob.MaxHp; } }
        public int SwingsResolved { get; private set; }
        public int HitsLanded { get; private set; }
        public int TotalDamage { get; private set; }
        /// <summary>Reward granted at death (null while the mob is alive).</summary>
        public MobKillReward? Reward { get; private set; }

        /// <summary>Builds the <see cref="DamageRequest"/> of a basic attack (coef 1.0) against this mob.</summary>
        public DamageRequest BuildBasicAttack(DerivedStats attacker, bool weakPoint)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            return new DamageRequest
            {
                AttackerLevel = attacker.Level,
                TargetLevel = Mob.Level,
                Power = attacker.Atk,
                Coefficient = 1.0,
                Kind = DamageKind.Physical,
                Type = DamageType.Physical,
                AttackerAccuracy = attacker.Accuracy,
                TargetEvasion = Mob.Evasion,
                GeometricHit = true,
                CritChance = attacker.CritChance,
                WeakPointCritBonus = weakPoint ? Mob.WeakPointCritBonus : 0,
                CritMultiplier = attacker.CritMultiplier,
                TargetDefense = Mob.Def,
                TargetResistance = 0,
                TargetCanBlock = false,
            };
        }

        /// <summary>Handles one geometric FPP hit (the blade intersected the mob's hit volume during swing <paramref name="swingId"/>).</summary>
        public CombatHitReport RegisterHit(int swingId, bool weakPoint = false)
        {
            int level = _player.Level;
            var report = new CombatHitReport
            {
                SwingId = swingId, WeakPoint = weakPoint, HpBefore = MobHp, HpAfter = MobHp, MaxHp = Mob.MaxHp,
                PlayerLevelBefore = level, PlayerLevelAfter = level,
            };
            if (MobDefeated)
            {
                report.Status = CombatHitStatus.MobAlreadyDefeated;
                return report;
            }

            if (!_resolvedSwings.Add(swingId))
            {
                report.Status = CombatHitStatus.DuplicateSwing;
                return report;
            }

            SwingsResolved++;
            DerivedStats attacker = _attackerStatsForLevel(level);
            DamageResult result = _resolver.Resolve(BuildBasicAttack(attacker, weakPoint), _random);
            report.Status = CombatHitStatus.Resolved;
            report.Damage = result;
            if (result.Landed)
            {
                HitsLanded++;
                int dealt = Math.Min(MobHp, result.Damage);
                TotalDamage += dealt;
                MobHp -= dealt;
            }

            report.HpAfter = MobHp;
            if (MobDefeated)
            {
                MobKillReward reward = MobRewardCalculator.Roll(_config, Mob, level, _random);
                Reward = reward;
                _player.AddExp(reward.Exp);
                _player.AddTalary(reward.Talary);
                report.Killed = true;
                report.Reward = reward;
                report.PlayerLevelAfter = _player.Level;
            }

            return report;
        }
    }
}
