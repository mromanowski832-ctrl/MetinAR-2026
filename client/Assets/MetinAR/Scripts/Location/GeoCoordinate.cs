using System;

namespace MetinAR.Location
{
    /// <summary>
    /// Immutable WGS84 geographic coordinate.
    /// Latitude and longitude are decimal degrees on the WGS84 ellipsoid/geographic frame
    /// as commonly reported by consumer GPS (no altitude).
    /// </summary>
    public readonly struct GeoCoordinate
    {
        /// <summary>
        /// Latitude in decimal degrees, WGS84. Valid range: [-90, 90].
        /// Positive is north of the equator; negative is south.
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// Longitude in decimal degrees, WGS84. Valid range: [-180, 180].
        /// Positive is east of the prime meridian; negative is west.
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Creates a validated geographic coordinate.
        /// </summary>
        /// <param name="latitude">Latitude in decimal degrees, WGS84. Must be in [-90, 90].</param>
        /// <param name="longitude">Longitude in decimal degrees, WGS84. Must be in [-180, 180].</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when latitude or longitude is NaN, positive infinity, negative infinity,
        /// or outside the valid range (latitude [-90, 90], longitude [-180, 180]).
        /// </exception>
        public GeoCoordinate(double latitude, double longitude)
        {
            if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude < -90.0 || latitude > 90.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(latitude),
                    latitude,
                    "Latitude must be a finite value between -90 and 90 degrees inclusive.");
            }

            if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude < -180.0 || longitude > 180.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(longitude),
                    longitude,
                    "Longitude must be a finite value between -180 and 180 degrees inclusive.");
            }

            Latitude = latitude;
            Longitude = longitude;
        }
    }
}
