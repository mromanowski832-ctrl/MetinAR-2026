using MetinAR.Server.Domain;
using MetinAR.Server.Geo;
using Xunit;

namespace MetinAR.Server.Tests;

public class GeoDistanceTests
{
    [Fact]
    public void Haversine_ZeroForSamePoint() =>
        Assert.Equal(0.0, GeoDistance.HaversineMeters(new GeoPoint(10.0, 10.0), new GeoPoint(10.0, 10.0)), 6);

    [Fact]
    public void Haversine_OneMillidegreeLatitudeIsAbout111Meters()
    {
        double d = GeoDistance.HaversineMeters(new GeoPoint(0, 0), new GeoPoint(0.001, 0));
        Assert.InRange(d, 111.1, 111.3);
    }

    [Fact]
    public void Haversine_AcrossAntimeridianIsShort()
    {
        double d = GeoDistance.HaversineMeters(new GeoPoint(0, 179.9995), new GeoPoint(0, -179.9995));
        Assert.InRange(d, 110.0, 112.5);
    }

    [Theory]
    [InlineData(0.0, 0.0, 500.0)]
    [InlineData(60.0, 25.0, 1000.0)]
    [InlineData(-45.0, -70.0, 50.0)]
    public void BoundingBox_ContainsEveryPointOnTheCircle(double lat, double lon, double radius)
    {
        var center = new GeoPoint(lat, lon);
        var box = GeoDistance.BoundingBox(center, radius);
        Assert.False(box.CrossesAntimeridianOrPole);
        // Points exactly at the radius in the four cardinal directions must lie inside the box.
        double dLat = radius / (Math.PI * GeoDistance.EarthRadiusMeters / 180.0);
        Assert.InRange(lat + dLat, box.MinLat, box.MaxLat + 1e-12);
        Assert.InRange(lat - dLat, box.MinLat - 1e-12, box.MaxLat);
        double dLon = dLat / Math.Cos(lat * Math.PI / 180.0);
        Assert.InRange(lon + dLon, box.MinLon, box.MaxLon + 1e-12);
        Assert.InRange(lon - dLon, box.MinLon - 1e-12, box.MaxLon);
    }

    [Fact]
    public void BoundingBox_FlagsAntimeridian()
    {
        var box = GeoDistance.BoundingBox(new GeoPoint(0, 179.9999), 100);
        Assert.True(box.CrossesAntimeridianOrPole);
    }

    [Fact]
    public void Coarsen_RoundsToThreeDecimals()
    {
        GeoPoint coarse = GeoDistance.Coarsen(new GeoPoint(12.345678, -98.765432));
        Assert.Equal(12.346, coarse.Latitude);
        Assert.Equal(-98.765, coarse.Longitude);
    }
}
