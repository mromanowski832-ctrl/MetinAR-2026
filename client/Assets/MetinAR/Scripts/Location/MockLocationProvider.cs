using System;

namespace MetinAR.Location
{
    /// <summary>
    /// Deterministic, Editor- and .NET-testable <see cref="ILocationProvider"/>.
    /// Pure C# — no UnityEngine dependency.
    /// Default start coordinate is Null Island (0, 0): a clearly synthetic point, not a real user home.
    /// </summary>
    public sealed class MockLocationProvider : ILocationProvider
    {
        /// <summary>
        /// Synthetic default: Null Island (0°N, 0°E). Not a real player home location.
        /// </summary>
        public static readonly GeoCoordinate DefaultSyntheticCoordinate = new GeoCoordinate(0.0, 0.0);

        private const double DefaultHorizontalAccuracyMeters = 5.0;

        private GeoCoordinate _coordinate;
        private double _horizontalAccuracyMeters;
        private double _timestampSeconds;
        private LocationProviderState _state;
        private bool _hasFix;

        /// <summary>
        /// Creates a mock provider at the synthetic default coordinate (0, 0).
        /// </summary>
        public MockLocationProvider()
            : this(DefaultSyntheticCoordinate)
        {
        }

        /// <summary>
        /// Creates a mock provider at the given start coordinate.
        /// </summary>
        /// <param name="startCoordinate">Initial mock position (must already be a valid <see cref="GeoCoordinate"/>).</param>
        public MockLocationProvider(GeoCoordinate startCoordinate)
        {
            _coordinate = startCoordinate;
            _horizontalAccuracyMeters = DefaultHorizontalAccuracyMeters;
            _timestampSeconds = 0.0;
            _state = LocationProviderState.Stopped;
            _hasFix = false;
        }

        /// <inheritdoc />
        public LocationProviderState State => _state;

        /// <summary>
        /// Current mock coordinate (updated by <see cref="SetCoordinate"/> / <see cref="MoveMeters"/>).
        /// </summary>
        public GeoCoordinate Coordinate => _coordinate;

        /// <summary>
        /// Horizontal accuracy reported for mock fixes (meters, &gt;= 0).
        /// </summary>
        public double HorizontalAccuracyMeters
        {
            get => _horizontalAccuracyMeters;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "Horizontal accuracy must be a finite value greater than or equal to 0 meters.");
                }

                _horizontalAccuracyMeters = value;
                RefreshFixIfRunning();
            }
        }

        /// <inheritdoc />
        public void Start()
        {
            _state = LocationProviderState.Running;
            _timestampSeconds += 1.0;
            _hasFix = true;
        }

        /// <inheritdoc />
        public void Stop()
        {
            _state = LocationProviderState.Stopped;
            _hasFix = false;
        }

        /// <inheritdoc />
        public bool TryGetLatestFix(out LocationFix fix)
        {
            if (!_hasFix || _state != LocationProviderState.Running)
            {
                fix = default;
                return false;
            }

            fix = new LocationFix(_coordinate, _horizontalAccuracyMeters, _timestampSeconds);
            return true;
        }

        /// <summary>
        /// Sets the mock coordinate. When running, advances the mock timestamp by 1 s
        /// and the next <see cref="TryGetLatestFix"/> reflects this position.
        /// </summary>
        public void SetCoordinate(GeoCoordinate coordinate)
        {
            _coordinate = coordinate;
            RefreshFixIfRunning();
        }

        /// <summary>
        /// Moves the mock position using <see cref="GeoMath.OffsetMeters"/>.
        /// When running, advances the mock timestamp by 1 s.
        /// </summary>
        /// <param name="northMeters">Northward displacement in meters (negative = south).</param>
        /// <param name="eastMeters">Eastward displacement in meters (negative = west).</param>
        public void MoveMeters(double northMeters, double eastMeters)
        {
            _coordinate = GeoMath.OffsetMeters(_coordinate, northMeters, eastMeters);
            RefreshFixIfRunning();
        }

        /// <summary>
        /// Sets the exact mock timestamp (seconds) used for subsequent fixes.
        /// Does not increment the timestamp on this call (unlike movement / coordinate updates).
        /// </summary>
        public void SetTimestampSeconds(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampSeconds),
                    timestampSeconds,
                    "Timestamp must be a finite value in seconds.");
            }

            _timestampSeconds = timestampSeconds;
            if (_state == LocationProviderState.Running)
            {
                _hasFix = true;
            }
        }

        /// <summary>
        /// When running, marks a fresh fix and advances the mock clock by 1 second.
        /// Used by coordinate / accuracy / movement updates — not by <see cref="SetTimestampSeconds"/>.
        /// </summary>
        private void RefreshFixIfRunning()
        {
            if (_state == LocationProviderState.Running)
            {
                _timestampSeconds += 1.0;
                _hasFix = true;
            }
        }
    }
}
