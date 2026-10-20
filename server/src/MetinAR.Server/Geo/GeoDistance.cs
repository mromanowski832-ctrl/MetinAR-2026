using MetinAR.Server.Domain;

namespace MetinAR.Server.Geo;

/// <summary>
/// Server-side great-circle helpers. Kept independent from the Unity client's GeoMath so the
/// server never depends on client source.
/// </summary>
public static class GeoDistance
{
    /// <summary>Mean Earth radius in meters (IUGG).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    private const double MetersPerDegreeLatitude = Math.PI * EarthRadiusMeters / 180.0;

    public static double HaversineMeters(GeoPoint a, GeoPoint b)
    {
        double lat1 = ToRadians(a.Latitude);
        double lat2 = ToRadians(b.Latitude);
        double dLat = lat2 - lat1;
        double dLon = ToRadians(b.Longitude - a.Longitude);

        double sinLat = Math.Sin(dLat / 2.0);
        double sinLon = Math.Sin(dLon / 2.0);
        double h = (sinLat * sinLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon);
        return 2.0 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    /// <summary>
    /// Conservative lat/lon bounding box that fully contains the circle. Used only as an indexed
    /// pre-filter; the exact haversine check is applied afterwards. Returns
    /// <c>crossesAntimeridianOrPole = true</c> when the longitude window cannot be expressed as a
    /// single range, in which case callers must not filter on longitude.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon, bool CrossesAntimeridianOrPole)
        BoundingBox(GeoPoint center, double radiusMeters)
    {
        double dLat = radiusMeters / MetersPerDegreeLatitude;
        double minLat = center.Latitude - dLat;
        double maxLat = center.Latitude + dLat;

        if (minLat <= -90.0 || maxLat >= 90.0)
        {
            return (Math.Max(minLat, -90.0), Math.Min(maxLat, 90.0), -180.0, 180.0, true);
        }

        double maxAbsLat = Math.Max(Math.Abs(minLat), Math.Abs(maxLat));
        double dLon = dLat / Math.Cos(ToRadians(maxAbsLat));
        double minLon = center.Longitude - dLon;
        double maxLon = center.Longitude + dLon;
        bool crosses = minLon < -180.0 || maxLon > 180.0;
        return crosses
            ? (minLat, maxLat, -180.0, 180.0, true)
            : (minLat, maxLat, minLon, maxLon, false);
    }

    /// <summary>
    /// Reduce precision of a position before it is stored (3 decimals ≈ 110 m at the equator).
    /// The server never needs an exact player location for this prototype.
    /// </summary>
    public static GeoPoint Coarsen(GeoPoint point, int decimals = 3) =>
        new(Math.Round(point.Latitude, decimals, MidpointRounding.AwayFromZero),
            Math.Round(point.Longitude, decimals, MidpointRounding.AwayFromZero));

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
