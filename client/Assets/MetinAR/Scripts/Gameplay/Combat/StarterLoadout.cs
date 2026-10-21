using System;

namespace MetinAR.Gameplay.Combat
{
    /// <summary>
    /// The S2 attacker: a warrior with the starter sword only (tables/items_starter.csv: <c>starter_sword</c>
    /// „Miecz Rekruta”, atk = 6, source shop|start; the other starter items come from the shop/drops in S4/S5)
    /// and no free points allocated yet (allocation UI is S6). Values are read from the approved tables.
    /// </summary>
    public static class StarterLoadout
    {
        /// <summary>Miecz Rekruta base attack (items_starter.csv).</summary>
        public const double StarterSwordAtk = 6;

        public static WeaponLoadout StarterSword()
        {
            return new WeaponLoadout { BaseAtk = StarterSwordAtk, TemperLevel = 0, ScalingStat = PrimaryStat.Might };
        }

        public static DerivedStats Stats(BalanceConfig config, ClassId classId, int level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var build = new CharacterBuild(classId, level, default(PrimaryStats));
            return DerivedStatsCalculator.Compute(config, build, StarterSword(), null);
        }
    }
}
