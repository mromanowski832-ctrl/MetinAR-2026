using System;
using MetinAR.Location;
using Xunit;

namespace GeoCore.Tests
{
    public class LocationFixTests
    {
        private static readonly GeoCoordinate Origin = new GeoCoordinate(0.0, 0.0);

        [Fact]
        public void ValidFix_StoresFields()
        {
            var fix = new LocationFix(Origin, 5.0, 100.0);
            Assert.Equal(0.0, fix.Coordinate.Latitude);
            Assert.Equal(0.0, fix.Coordinate.Longitude);
            Assert.Equal(5.0, fix.HorizontalAccuracyMeters);
            Assert.Equal(100.0, fix.TimestampSeconds);
        }

        [Fact]
        public void ZeroAccuracy_IsAllowed()
        {
            var fix = new LocationFix(Origin, 0.0, 1.0);
            Assert.Equal(0.0, fix.HorizontalAccuracyMeters);
        }

        [Fact]
        public void NegativeAccuracy_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocationFix(Origin, -0.1, 1.0));
        }

        [Fact]
        public void Accuracy_NaN_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocationFix(Origin, double.NaN, 1.0));
        }

        [Fact]
        public void Accuracy_Infinity_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new LocationFix(Origin, double.PositiveInfinity, 1.0));
        }

        [Fact]
        public void Timestamp_NaN_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocationFix(Origin, 1.0, double.NaN));
        }

        [Fact]
        public void Timestamp_Infinity_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new LocationFix(Origin, 1.0, double.NegativeInfinity));
        }
    }
}
