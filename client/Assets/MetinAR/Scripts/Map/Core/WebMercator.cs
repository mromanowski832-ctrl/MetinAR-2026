using System;
using System.Collections.Generic;
using MetinAR.Location;

namespace MetinAR.Map
{
    /// <summary>
    /// Pure spherical web-mercator (EPSG:3857) tile math, XYZ scheme (Y down).
    /// "Normalized tile coordinates" (u, v) are in [0, 1] within one tile: u east, v south.
    /// </summary>
    public static class WebMercator
    {
        /// <summary>Latitude limit of the square web-mercator world (degrees).</summary>
        public const double MaxLatitude = 85.0511287798066;

        /// <summary>
        /// Fractional global tile coordinates of a point at a zoom. Integer part = tile, fraction = position inside.
        /// Latitude is clamped to <see cref="MaxLatitude"/>.
        /// </summary>
        public static void ToTileFraction(double latitude, double longitude, int zoom, out double tileX, out double tileY)
        {
            if (zoom < 0 || zoom > TileId.MaxZoom)
            {
                throw new ArgumentOutOfRangeException(nameof(zoom));
            }

            double lat = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, latitude));
            double n = 1L << zoom;
            tileX = (longitude + 180.0) / 360.0 * n;
            double latRad = lat * Math.PI / 180.0;
            tileY = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
        }

        /// <summary>Tile containing the coordinate at <paramref name="zoom"/> (edges clamp into range).</summary>
        public static TileId TileFor(GeoCoordinate coordinate, int zoom)
        {
            ToTileFraction(coordinate.Latitude, coordinate.Longitude, zoom, out double fx, out double fy);
            long n = 1L << zoom;
            int x = (int)Math.Max(0, Math.Min(n - 1, Math.Floor(fx)));
            int y = (int)Math.Max(0, Math.Min(n - 1, Math.Floor(fy)));
            return new TileId(zoom, x, y);
        }

        /// <summary>Converts fractional global tile coordinates back to latitude/longitude (degrees).</summary>
        public static void FromTileFraction(double tileX, double tileY, int zoom, out double latitude, out double longitude)
        {
            double n = 1L << zoom;
            longitude = tileX / n * 360.0 - 180.0;
            double mercN = Math.PI * (1.0 - 2.0 * tileY / n);
            latitude = Math.Atan(Math.Sinh(mercN)) * 180.0 / Math.PI;
        }

        /// <summary>Converts normalized coordinates inside a tile (u east, v south, 0..1) to latitude/longitude.</summary>
        public static void TileNormalizedToGeo(TileId tile, double u, double v, out double latitude, out double longitude)
        {
            FromTileFraction(tile.X + u, tile.Y + v, tile.Z, out latitude, out longitude);
        }

        /// <summary>Geographic bounds of a tile.</summary>
        public static GeoBounds Bounds(TileId tile)
        {
            FromTileFraction(tile.X, tile.Y, tile.Z, out double north, out double west);
            FromTileFraction(tile.X + 1, tile.Y + 1, tile.Z, out double south, out double east);
            return new GeoBounds(west, south, east, north);
        }

        /// <summary>Union bounds of several tiles (no antimeridian wrap handling; prototype scope).</summary>
        public static GeoBounds Bounds(IReadOnlyList<TileId> tiles)
        {
            if (tiles == null || tiles.Count == 0)
            {
                throw new ArgumentException("At least one tile is required.", nameof(tiles));
            }

            double w = double.MaxValue, s = double.MaxValue, e = double.MinValue, n = double.MinValue;
            foreach (TileId t in tiles)
            {
                GeoBounds b = Bounds(t);
                w = Math.Min(w, b.West);
                s = Math.Min(s, b.South);
                e = Math.Max(e, b.East);
                n = Math.Max(n, b.North);
            }

            return new GeoBounds(w, s, e, n);
        }

        /// <summary>
        /// Square neighborhood of (2r+1)^2 tiles around <paramref name="center"/>, row-major from north-west.
        /// X wraps around the antimeridian; rows beyond the poles are skipped.
        /// </summary>
        public static List<TileId> Neighborhood(TileId center, int radius)
        {
            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            long n = center.TilesPerAxis;
            var result = new List<TileId>((2 * radius + 1) * (2 * radius + 1));
            for (int dy = -radius; dy <= radius; dy++)
            {
                long y = center.Y + dy;
                if (y < 0 || y >= n)
                {
                    continue;
                }

                for (int dx = -radius; dx <= radius; dx++)
                {
                    long x = ((center.X + dx) % n + n) % n;
                    var id = new TileId(center.Z, (int)x, (int)y);
                    if (!result.Contains(id))
                    {
                        result.Add(id);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Distinct tiles at <paramref name="sourceZoom"/> that cover the given tiles (ancestors when the source
        /// zoom is lower, e.g. a z14-max dataset displayed at z16). Order is stable.
        /// </summary>
        public static List<TileId> SourceTilesFor(IReadOnlyList<TileId> displayTiles, int sourceZoom)
        {
            var result = new List<TileId>();
            foreach (TileId t in displayTiles)
            {
                if (sourceZoom > t.Z)
                {
                    throw new ArgumentOutOfRangeException(nameof(sourceZoom), "Source zoom above display zoom is not supported.");
                }

                TileId a = t.AncestorAt(sourceZoom);
                if (!result.Contains(a))
                {
                    result.Add(a);
                }
            }

            return result;
        }

        /// <summary>Approximate ground width of one tile in meters at a latitude (spherical model).</summary>
        public static double TileWidthMeters(int zoom, double latitude)
        {
            double equator = 2.0 * Math.PI * GeoMath.EarthRadiusMeters;
            return equator * Math.Cos(latitude * Math.PI / 180.0) / (1L << zoom);
        }
    }
}
