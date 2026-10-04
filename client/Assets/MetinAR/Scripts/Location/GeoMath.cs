using System;

namespace MetinAR.Location
{
    /// <summary>
    /// Pure geographic math helpers for short-range GPS distances and local offsets.
    /// Uses a spherical Earth model (not a full Vincenty/WGS84 ellipsoid). Units are meters
    /// for distances/offsets and decimal degrees for coordinates.
    /// </summary>
    public static class GeoMath
    {
        /// <summary>
        /// IUGG mean Earth radius (authalic / volumetric mean), in meters.
        /// Commonly cited value 6371008.8 m. Distance and offset formulas treat Earth as a
        /// sphere of this radius, not an ellipsoid.
        /// </summary>
        public const double EarthRadiusMeters = 6371008.8;

        /// <summary>
        /// Meters per degree of latitude on the sphere:
        /// EarthRadiusMeters * π / 180.
        /// </summary>
        public static readonly double MetersPerDegreeLatitude =
            EarthRadiusMeters * Math.PI / 180.0;

        /// <summary>
        /// Minimum absolute cosine of latitude (radians) allowed in OffsetMeters.
        /// Near the poles, east/west meters map to unbounded longitude change; we reject
        /// origins where |cos(latitude)| is below this threshold.
        /// </summary>
        private const double MinimumCosLatitudeForOffset = 1e-10;

        /// <summary>
        /// Great-circle distance between two coordinates using the haversine formula on a
        /// spherical Earth of radius <see cref="EarthRadiusMeters"/>.
        /// </summary>
        /// <param name="a">First coordinate (decimal degrees, WGS84).</param>
        /// <param name="b">Second coordinate (decimal degrees, WGS84).</param>
        /// <returns>Distance in meters. Identical inputs yield exactly 0.</returns>
        public static double DistanceMeters(GeoCoordinate a, GeoCoordinate b)
        {
            if (a.Latitude == b.Latitude && a.Longitude == b.Longitude)
            {
                return 0.0;
            }

            double lat1Radians = DegreesToRadians(a.Latitude);
            double lat2Radians = DegreesToRadians(b.Latitude);
            double deltaLatitudeRadians = DegreesToRadians(b.Latitude - a.Latitude);
            double deltaLongitudeRadians = DegreesToRadians(b.Longitude - a.Longitude);

            double sinHalfDeltaLat = Math.Sin(deltaLatitudeRadians / 2.0);
            double sinHalfDeltaLon = Math.Sin(deltaLongitudeRadians / 2.0);

            double haversineCentralAngle =
                sinHalfDeltaLat * sinHalfDeltaLat
                + Math.Cos(lat1Radians) * Math.Cos(lat2Radians)
                  * sinHalfDeltaLon * sinHalfDeltaLon;

            double centralAngleRadians =
                2.0 * Math.Atan2(Math.Sqrt(haversineCentralAngle), Math.Sqrt(1.0 - haversineCentralAngle));

            return EarthRadiusMeters * centralAngleRadians;
        }

        /// <summary>
        /// Applies a local tangent-plane offset: positive <paramref name="northMeters"/> is north,
        /// positive <paramref name="eastMeters"/> is east. Conversion uses the spherical
        /// <see cref="MetersPerDegreeLatitude"/> and longitude scale
        /// MetersPerDegreeLatitude * cos(latitude).
        /// </summary>
        /// <param name="origin">Starting coordinate (decimal degrees, WGS84).</param>
        /// <param name="northMeters">Northward displacement in meters (negative = south).</param>
        /// <param name="eastMeters">Eastward displacement in meters (negative = west).</param>
        /// <returns>New coordinate after offset; longitude normalized to [-180, 180].</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the origin is too close to a pole (|cos(latitude)| below
        /// <see cref="MinimumCosLatitudeForOffset"/>), or when the resulting latitude
        /// would fall outside [-90, 90].
        /// </exception>
        public static GeoCoordinate OffsetMeters(
            GeoCoordinate origin,
            double northMeters,
            double eastMeters)
        {
            double latitudeRadians = DegreesToRadians(origin.Latitude);
            double cosLatitude = Math.Cos(latitudeRadians);

            if (Math.Abs(cosLatitude) < MinimumCosLatitudeForOffset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(origin),
                    origin.Latitude,
                    "OffsetMeters cannot apply an east/west component near the poles "
                    + "(|cos(latitude)| too small); latitude is too close to ±90°.");
            }

            double deltaLatitudeDegrees = northMeters / MetersPerDegreeLatitude;
            double metersPerDegreeLongitude = MetersPerDegreeLatitude * cosLatitude;
            double deltaLongitudeDegrees = eastMeters / metersPerDegreeLongitude;

            double newLatitude = origin.Latitude + deltaLatitudeDegrees;
            if (newLatitude < -90.0 || newLatitude > 90.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(northMeters),
                    northMeters,
                    "Offset would produce a latitude outside [-90, 90] degrees.");
            }

            double newLongitude = NormalizeLongitudeDegrees(origin.Longitude + deltaLongitudeDegrees);
            return new GeoCoordinate(newLatitude, newLongitude);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        /// <summary>
        /// Normalizes longitude into the closed-open range used by <see cref="GeoCoordinate"/>:
        /// [-180, 180]. Values of exactly ±180 are left as-is when already in range;
        /// values outside are wrapped.
        /// </summary>
        private static double NormalizeLongitudeDegrees(double longitudeDegrees)
        {
            // Bring into (-180, 180] then clamp edge cases into [-180, 180].
            double wrapped = longitudeDegrees % 360.0;
            if (wrapped > 180.0)
            {
                wrapped -= 360.0;
            }
            else if (wrapped < -180.0)
            {
                wrapped += 360.0;
            }

            // % in C# can leave -0; treat -180 and 180 as valid endpoints.
            if (wrapped < -180.0)
            {
                wrapped = -180.0;
            }
            else if (wrapped > 180.0)
            {
                wrapped = 180.0;
            }

            return wrapped;
        }
    }
}
