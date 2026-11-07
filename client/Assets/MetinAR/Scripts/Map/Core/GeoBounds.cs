using System;

namespace MetinAR.Map
{
    /// <summary>Axis-aligned geographic bounds in decimal degrees (WGS84).</summary>
    public readonly struct GeoBounds
    {
        public GeoBounds(double west, double south, double east, double north)
        {
            if (south > north)
            {
                throw new ArgumentException("South must be <= north.");
            }

            West = west;
            South = south;
            East = east;
            North = north;
        }

        public double West { get; }

        public double South { get; }

        public double East { get; }

        public double North { get; }

        public bool Contains(double latitude, double longitude)
        {
            return latitude >= South && latitude <= North && longitude >= West && longitude <= East;
        }

        public bool Intersects(GeoBounds other)
        {
            return !(other.West > East || other.East < West || other.South > North || other.North < South);
        }

        public override string ToString()
        {
            return "[" + West + "," + South + "," + East + "," + North + "]";
        }
    }
}
