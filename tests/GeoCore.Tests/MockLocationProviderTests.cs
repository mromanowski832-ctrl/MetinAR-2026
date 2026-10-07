using System;
using MetinAR.Location;
using Xunit;

namespace GeoCore.Tests
{
    public class MockLocationProviderTests
    {
        private static readonly GeoCoordinate Warsaw = new GeoCoordinate(52.2297, 21.0122);

        [Fact]
        public void DefaultConstructor_UsesSyntheticNullIsland()
        {
            var provider = new MockLocationProvider();
            Assert.Equal(0.0, provider.Coordinate.Latitude);
            Assert.Equal(0.0, provider.Coordinate.Longitude);
            Assert.Equal(LocationProviderState.Stopped, provider.State);
            Assert.False(provider.TryGetLatestFix(out _));
        }

        [Fact]
        public void Start_ProducesDeterministicValidFix()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.SetTimestampSeconds(42.0);
            provider.Start();

            Assert.Equal(LocationProviderState.Running, provider.State);
            Assert.True(provider.TryGetLatestFix(out LocationFix fix));
            Assert.Equal(Warsaw.Latitude, fix.Coordinate.Latitude);
            Assert.Equal(Warsaw.Longitude, fix.Coordinate.Longitude);
            Assert.True(fix.HorizontalAccuracyMeters >= 0.0);
            Assert.False(double.IsNaN(fix.TimestampSeconds));
            Assert.False(double.IsInfinity(fix.TimestampSeconds));
        }

        [Fact]
        public void Stop_ClearsFixAvailability()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.Start();
            provider.Stop();

            Assert.Equal(LocationProviderState.Stopped, provider.State);
            Assert.False(provider.TryGetLatestFix(out _));
        }

        [Fact]
        public void SetCoordinate_UpdatesRunningFix()
        {
            var provider = new MockLocationProvider();
            provider.Start();
            provider.SetCoordinate(Warsaw);

            Assert.True(provider.TryGetLatestFix(out LocationFix fix));
            Assert.Equal(Warsaw.Latitude, fix.Coordinate.Latitude);
            Assert.Equal(Warsaw.Longitude, fix.Coordinate.Longitude);
        }

        [Fact]
        public void MoveMeters_UsesGeoMathOffset()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.Start();
            provider.MoveMeters(50.0, 0.0);

            Assert.True(provider.TryGetLatestFix(out LocationFix fix));
            Assert.True(fix.Coordinate.Latitude > Warsaw.Latitude);
            Assert.Equal(Warsaw.Longitude, fix.Coordinate.Longitude);

            double distance = GeoMath.DistanceMeters(Warsaw, fix.Coordinate);
            Assert.InRange(Math.Abs(distance - 50.0), 0.0, 0.05);
        }

        [Fact]
        public void MoveMeters_East_UpdatesLongitude()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.Start();
            provider.MoveMeters(0.0, 50.0);

            Assert.True(provider.TryGetLatestFix(out LocationFix fix));
            Assert.True(fix.Coordinate.Longitude > Warsaw.Longitude);
            Assert.Equal(Warsaw.Latitude, fix.Coordinate.Latitude);
        }

        [Fact]
        public void NegativeAccuracy_IsRejected()
        {
            var provider = new MockLocationProvider();
            Assert.Throws<ArgumentOutOfRangeException>(() => provider.HorizontalAccuracyMeters = -1.0);
        }

        [Fact]
        public void Timestamp_NaN_IsRejected()
        {
            var provider = new MockLocationProvider();
            Assert.Throws<ArgumentOutOfRangeException>(() => provider.SetTimestampSeconds(double.NaN));
        }

        [Fact]
        public void SetTimestampSeconds_WhileRunning_SetsExactTimestampWithoutIncrement()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.Start();
            Assert.True(provider.TryGetLatestFix(out LocationFix before));

            const double exact = 12345.678;
            provider.SetTimestampSeconds(exact);

            Assert.True(provider.TryGetLatestFix(out LocationFix after));
            Assert.Equal(exact, after.TimestampSeconds);
            Assert.NotEqual(before.TimestampSeconds + 1.0, after.TimestampSeconds);
        }

        [Fact]
        public void MoveMeters_WhileRunning_AdvancesTimestamp()
        {
            var provider = new MockLocationProvider(Warsaw);
            provider.Start();
            provider.SetTimestampSeconds(100.0);
            Assert.True(provider.TryGetLatestFix(out LocationFix before));
            Assert.Equal(100.0, before.TimestampSeconds);

            provider.MoveMeters(1.0, 0.0);

            Assert.True(provider.TryGetLatestFix(out LocationFix after));
            Assert.Equal(101.0, after.TimestampSeconds);
        }
    }
}
