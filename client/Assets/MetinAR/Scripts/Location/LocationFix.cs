using System;

namespace MetinAR.Location
{
    /// <summary>
    /// Immutable GPS/location sample: coordinate, horizontal accuracy, and timestamp.
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public readonly struct LocationFix
    {
        /// <summary>
        /// WGS84 geographic position for this fix.
        /// </summary>
        public GeoCoordinate Coordinate { get; }

        /// <summary>
        /// Estimated horizontal accuracy radius in meters. Always &gt;= 0.
        /// </summary>
        public double HorizontalAccuracyMeters { get; }

        /// <summary>
        /// Timestamp in seconds (UTC Unix epoch or provider-relative monotonic seconds).
        /// Must be finite; not used for geo math.
        /// </summary>
        public double TimestampSeconds { get; }

        /// <summary>
        /// Creates a validated location fix.
        /// </summary>
        /// <param name="coordinate">Validated geographic coordinate.</param>
        /// <param name="horizontalAccuracyMeters">Horizontal accuracy in meters; must be finite and &gt;= 0.</param>
        /// <param name="timestampSeconds">Finite timestamp in seconds.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when accuracy or timestamp is NaN/Infinity, or accuracy is negative.
        /// </exception>
        public LocationFix(
            GeoCoordinate coordinate,
            double horizontalAccuracyMeters,
            double timestampSeconds)
        {
            if (double.IsNaN(horizontalAccuracyMeters)
                || double.IsInfinity(horizontalAccuracyMeters)
                || horizontalAccuracyMeters < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(horizontalAccuracyMeters),
                    horizontalAccuracyMeters,
                    "Horizontal accuracy must be a finite value greater than or equal to 0 meters.");
            }

            if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampSeconds),
                    timestampSeconds,
                    "Timestamp must be a finite value in seconds.");
            }

            Coordinate = coordinate;
            HorizontalAccuracyMeters = horizontalAccuracyMeters;
            TimestampSeconds = timestampSeconds;
        }
    }
}
