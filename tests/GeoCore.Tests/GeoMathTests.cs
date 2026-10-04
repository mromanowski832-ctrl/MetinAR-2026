using System;
using MetinAR.Location;
using Xunit;

namespace GeoCore.Tests
{
    public class GeoMathTests
    {
        private const double TightToleranceMeters = 0.05;

        private static readonly GeoCoordinate Warsaw = new GeoCoordinate(52.2297, 21.0122);

        [Fact]
        public void IdenticalCoordinates_ReturnZeroMeters()
        {
            double distance = GeoMath.DistanceMeters(Warsaw, Warsaw);
            Assert.Equal(0.0, distance);
        }

        [Fact]
        public void OffsetMeters_FiftyNorth_IsApproximatelyFiftyMeters()
        {
            GeoCoordinate moved = GeoMath.OffsetMeters(Warsaw, 50.0, 0.0);
            double distance = GeoMath.DistanceMeters(Warsaw, moved);
            Assert.InRange(Math.Abs(distance - 50.0), 0.0, TightToleranceMeters);
        }

        [Fact]
        public void OffsetMeters_FiftyEast_IsApproximatelyFiftyMeters()
        {
            GeoCoordinate moved = GeoMath.OffsetMeters(Warsaw, 0.0, 50.0);
            double distance = GeoMath.DistanceMeters(Warsaw, moved);
            Assert.InRange(Math.Abs(distance - 50.0), 0.0, TightToleranceMeters);
        }

        [Fact]
        public void NorthOffset_IncreasesLatitude()
        {
            GeoCoordinate moved = GeoMath.OffsetMeters(Warsaw, 50.0, 0.0);
            Assert.True(moved.Latitude > Warsaw.Latitude);
            Assert.Equal(Warsaw.Longitude, moved.Longitude);
        }

        [Fact]
        public void EastOffset_IncreasesLongitude_AtNormalNorthernLatitude()
        {
            GeoCoordinate moved = GeoMath.OffsetMeters(Warsaw, 0.0, 50.0);
            Assert.True(moved.Longitude > Warsaw.Longitude);
            Assert.Equal(Warsaw.Latitude, moved.Latitude);
        }

        [Fact]
        public void Latitude_Positive91_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(91.0, 0.0));
        }

        [Fact]
        public void Latitude_Negative91_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(-91.0, 0.0));
        }

        [Fact]
        public void Longitude_Positive181_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(0.0, 181.0));
        }

        [Fact]
        public void Longitude_Negative181_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(0.0, -181.0));
        }

        [Fact]
        public void Latitude_NaN_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(double.NaN, 0.0));
        }

        [Fact]
        public void Longitude_NaN_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(0.0, double.NaN));
        }

        [Fact]
        public void Latitude_PositiveInfinity_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(double.PositiveInfinity, 0.0));
        }

        [Fact]
        public void Longitude_NegativeInfinity_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(0.0, double.NegativeInfinity));
        }

        [Fact]
        public void NorthMeters_NaN_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoMath.OffsetMeters(Warsaw, double.NaN, 0.0));
        }

        [Fact]
        public void EastMeters_Infinity_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoMath.OffsetMeters(Warsaw, 0.0, double.PositiveInfinity));
        }

        [Fact]
        public void DistanceMeters_IsSymmetric()
        {
            GeoCoordinate other = GeoMath.OffsetMeters(Warsaw, 30.0, 40.0);
            double forward = GeoMath.DistanceMeters(Warsaw, other);
            double reverse = GeoMath.DistanceMeters(other, Warsaw);
            Assert.Equal(forward, reverse);
        }

        [Fact]
        public void ShortOffset_AroundFiftyMeters_StaysWithinTightTolerance()
        {
            GeoCoordinate moved = GeoMath.OffsetMeters(Warsaw, 30.0, 40.0);
            double distance = GeoMath.DistanceMeters(Warsaw, moved);
            Assert.InRange(Math.Abs(distance - 50.0), 0.0, TightToleranceMeters);
        }
    }
}
