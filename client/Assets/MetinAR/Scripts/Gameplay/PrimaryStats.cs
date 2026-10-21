using System;

namespace MetinAR.Gameplay
{
    /// <summary>Primary stats (CHARACTER_PROGRESSION.md §1.1): Might (Moc), Finesse (Zwinność), Spirit (Duch), Vigor (Krzepa).</summary>
    public enum PrimaryStat
    {
        Might = 0,
        Finesse = 1,
        Spirit = 2,
        Vigor = 3,
    }

    public readonly struct PrimaryStats : IEquatable<PrimaryStats>
    {
        public readonly int Might;
        public readonly int Finesse;
        public readonly int Spirit;
        public readonly int Vigor;

        public PrimaryStats(int might, int finesse, int spirit, int vigor)
        {
            Might = might;
            Finesse = finesse;
            Spirit = spirit;
            Vigor = vigor;
        }

        public int Total { get { return Might + Finesse + Spirit + Vigor; } }

        public int Get(PrimaryStat stat)
        {
            switch (stat)
            {
                case PrimaryStat.Might: return Might;
                case PrimaryStat.Finesse: return Finesse;
                case PrimaryStat.Spirit: return Spirit;
                case PrimaryStat.Vigor: return Vigor;
                default: throw new ArgumentOutOfRangeException(nameof(stat), stat, null);
            }
        }

        public PrimaryStats With(PrimaryStat stat, int value)
        {
            switch (stat)
            {
                case PrimaryStat.Might: return new PrimaryStats(value, Finesse, Spirit, Vigor);
                case PrimaryStat.Finesse: return new PrimaryStats(Might, value, Spirit, Vigor);
                case PrimaryStat.Spirit: return new PrimaryStats(Might, Finesse, value, Vigor);
                case PrimaryStat.Vigor: return new PrimaryStats(Might, Finesse, Spirit, value);
                default: throw new ArgumentOutOfRangeException(nameof(stat), stat, null);
            }
        }

        public PrimaryStats Add(PrimaryStat stat, int amount)
        {
            return With(stat, Get(stat) + amount);
        }

        public static PrimaryStats operator +(PrimaryStats a, PrimaryStats b)
        {
            return new PrimaryStats(a.Might + b.Might, a.Finesse + b.Finesse, a.Spirit + b.Spirit, a.Vigor + b.Vigor);
        }

        public bool Equals(PrimaryStats other)
        {
            return Might == other.Might && Finesse == other.Finesse && Spirit == other.Spirit && Vigor == other.Vigor;
        }

        public override bool Equals(object obj) { return obj is PrimaryStats && Equals((PrimaryStats)obj); }

        public override int GetHashCode()
        {
            unchecked { return ((Might * 397 ^ Finesse) * 397 ^ Spirit) * 397 ^ Vigor; }
        }

        public override string ToString()
        {
            return "Might=" + Might + " Finesse=" + Finesse + " Spirit=" + Spirit + " Vigor=" + Vigor;
        }
    }
}
