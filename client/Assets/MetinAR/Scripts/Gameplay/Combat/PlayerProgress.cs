using System;

namespace MetinAR.Gameplay.Combat
{
    /// <summary>
    /// Local character progress for S2: total EXP (level derived from <see cref="ExpCurve"/>, max level 99) and the
    /// talary balance (<c>long</c>, no fractions, ECONOMY.md §1). Kept on the client in v1; the server becomes
    /// authoritative in S11 (#56) / #39. Death penalty, stat allocation and skill points are later stages (S3/S6).
    /// </summary>
    public sealed class PlayerProgress
    {
        private readonly ExpCurve _curve;

        public PlayerProgress(ExpCurve curve, ClassId classId, long totalExp = 0, long talary = 0)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (totalExp < 0) throw new ArgumentOutOfRangeException(nameof(totalExp));
            if (talary < 0) throw new ArgumentOutOfRangeException(nameof(talary));
            _curve = curve;
            Class = classId;
            TotalExp = Math.Min(totalExp, curve.TotalExpToMaxLevel);
            Talary = talary;
        }

        public ExpCurve Curve { get { return _curve; } }
        public ClassId Class { get; }
        public long TotalExp { get; private set; }
        public long Talary { get; private set; }

        public int Level { get { return _curve.LevelForTotalExp(TotalExp); } }
        public bool IsMaxLevel { get { return Level >= _curve.MaxLevel; } }
        public long ExpIntoLevel { get { return _curve.ExpIntoLevel(TotalExp); } }
        public long ExpToNextLevel { get { return _curve.ExpToNext(Level); } }

        /// <summary>Filled HUD orbs 0…4, fractional (CHARACTER_PROGRESSION.md §2.1).</summary>
        public double FilledHudOrbs { get { return _curve.FilledHudOrbs(TotalExp); } }

        /// <summary>Adds EXP (capped at the total for max level) and returns the number of levels gained.</summary>
        public int AddExp(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int before = Level;
            TotalExp = Math.Min(_curve.TotalExpToMaxLevel, TotalExp + amount);
            return Level - before;
        }

        public void AddTalary(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Talary = checked(Talary + amount);
        }
    }

    /// <summary>HUD orb fill helper: orb <c>i</c> (0…3) shows clamp01(filledOrbs − i).</summary>
    public static class HudExpOrbs
    {
        public static double OrbFill(double filledOrbs, int orbIndex)
        {
            if (orbIndex < 0) throw new ArgumentOutOfRangeException(nameof(orbIndex));
            return GameplayMath.Clamp01(filledOrbs - orbIndex);
        }
    }
}
