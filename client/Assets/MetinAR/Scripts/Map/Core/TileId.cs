using System;

namespace MetinAR.Map
{
    /// <summary>
    /// Web-mercator (XYZ / "slippy map") tile address. X grows east, Y grows south, zoom 0 = one tile.
    /// Provider-independent: no URL, format or vendor knowledge.
    /// </summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public const int MaxZoom = 24;

        public TileId(int z, int x, int y)
        {
            if (z < 0 || z > MaxZoom)
            {
                throw new ArgumentOutOfRangeException(nameof(z), z, "Zoom must be in [0, " + MaxZoom + "].");
            }

            long n = 1L << z;
            if (x < 0 || x >= n)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "X must be in [0, 2^z).");
            }

            if (y < 0 || y >= n)
            {
                throw new ArgumentOutOfRangeException(nameof(y), y, "Y must be in [0, 2^z).");
            }

            Z = z;
            X = x;
            Y = y;
        }

        public int Z { get; }

        public int X { get; }

        public int Y { get; }

        /// <summary>Number of tiles along one axis at this zoom (2^z).</summary>
        public long TilesPerAxis => 1L << Z;

        /// <summary>Ancestor tile at a lower or equal zoom.</summary>
        public TileId AncestorAt(int zoom)
        {
            if (zoom < 0 || zoom > Z)
            {
                throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Ancestor zoom must be in [0, Z].");
            }

            int shift = Z - zoom;
            return new TileId(zoom, X >> shift, Y >> shift);
        }

        public bool Equals(TileId other)
        {
            return Z == other.Z && X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is TileId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Z;
                hash = (hash * 397) ^ X;
                hash = (hash * 397) ^ Y;
                return hash;
            }
        }

        public static bool operator ==(TileId a, TileId b) => a.Equals(b);

        public static bool operator !=(TileId a, TileId b) => !a.Equals(b);

        public override string ToString()
        {
            return Z + "/" + X + "/" + Y;
        }
    }
}
