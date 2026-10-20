using MetinAR.Location;
using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class TileMathTests
{
    [Fact]
    public void Zoom0_IsSingleWorldTile()
    {
        var t = WebMercator.TileFor(new GeoCoordinate(10, 20), 0);
        Assert.Equal(new TileId(0, 0, 0), t);
        GeoBounds b = WebMercator.Bounds(t);
        Assert.Equal(-180, b.West, 9);
        Assert.Equal(180, b.East, 9);
        Assert.Equal(WebMercator.MaxLatitude, b.North, 6);
        Assert.Equal(-WebMercator.MaxLatitude, b.South, 6);
    }

    [Theory]
    // Known XYZ tiles for public landmarks (values from the standard slippy-map formula).
    [InlineData(50.0617, 19.9373, 14, 9099, 5552)]   // Kraków Main Market Square
    [InlineData(52.2297, 21.0122, 16, 36593, 21579)] // Warsaw centre
    [InlineData(0.0, 0.0, 1, 1, 1)]
    [InlineData(-33.8688, 151.2093, 10, 942, 614)]   // Sydney
    public void TileFor_MatchesKnownTiles(double lat, double lon, int z, int x, int y)
    {
        Assert.Equal(new TileId(z, x, y), WebMercator.TileFor(new GeoCoordinate(lat, lon), z));
    }

    [Fact]
    public void Bounds_ContainTheirPoint_AndRoundTrip()
    {
        var p = new GeoCoordinate(50.0617, 19.9373);
        for (int z = 0; z <= 18; z++)
        {
            TileId t = WebMercator.TileFor(p, z);
            GeoBounds b = WebMercator.Bounds(t);
            Assert.True(b.Contains(p.Latitude, p.Longitude), "z=" + z);
            WebMercator.ToTileFraction(p.Latitude, p.Longitude, z, out double fx, out double fy);
            WebMercator.FromTileFraction(fx, fy, z, out double lat, out double lon);
            Assert.Equal(p.Latitude, lat, 9);
            Assert.Equal(p.Longitude, lon, 9);
        }
    }

    [Fact]
    public void TileNormalizedCorners_MatchBounds()
    {
        var t = new TileId(14, 9099, 5552);
        GeoBounds b = WebMercator.Bounds(t);
        WebMercator.TileNormalizedToGeo(t, 0, 0, out double nwLat, out double nwLon);
        WebMercator.TileNormalizedToGeo(t, 1, 1, out double seLat, out double seLon);
        Assert.Equal(b.North, nwLat, 12);
        Assert.Equal(b.West, nwLon, 12);
        Assert.Equal(b.South, seLat, 12);
        Assert.Equal(b.East, seLon, 12);
    }

    [Fact]
    public void Neighborhood_Is3x3RowMajor_AndWrapsX()
    {
        var c = new TileId(16, 36397, 22208);
        var n = WebMercator.Neighborhood(c, 1);
        Assert.Equal(9, n.Count);
        Assert.Equal(new TileId(16, 36396, 22207), n[0]);
        Assert.Equal(c, n[4]);
        Assert.Equal(new TileId(16, 36398, 22209), n[8]);

        var edge = WebMercator.Neighborhood(new TileId(3, 0, 0), 1);
        Assert.Equal(6, edge.Count); // top row skipped at the pole
        Assert.Contains(new TileId(3, 7, 0), edge); // wrapped west
    }

    [Fact]
    public void SourceTiles_AreDistinctAncestors()
    {
        var display = WebMercator.Neighborhood(new TileId(16, 36397, 22208), 1);
        var src = WebMercator.SourceTilesFor(display, 14);
        Assert.InRange(src.Count, 1, 4);
        Assert.All(src, s => Assert.Equal(14, s.Z));
        Assert.Contains(new TileId(14, 9099, 5552), src);
        Assert.Equal(src.Count, src.Distinct().Count());
    }

    [Fact]
    public void TileId_ValidatesRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileId(2, 4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileId(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileId(25, 0, 0));
        Assert.Equal(new TileId(14, 9099, 5552), new TileId(16, 36397, 22208).AncestorAt(14));
    }

    [Fact]
    public void LocalFrame_FollowsWorldMapOriginContract()
    {
        var origin = new GeoCoordinate(50.0617, 19.9373);
        var frame = new LocalTangentFrame(origin);

        frame.ToLocalMeters(origin, out double e0, out double n0);
        Assert.Equal(0, e0, 9);
        Assert.Equal(0, n0, 9);

        // Same algorithm as WorldMapOrigin.GetLocalMeters: signed distance along meridian / parallel.
        var p = GeoMath.OffsetMeters(origin, 120, -80);
        frame.ToLocalMeters(p, out double e, out double n);
        double expectedNorth = GeoMath.DistanceMeters(origin, new GeoCoordinate(p.Latitude, origin.Longitude));
        double expectedEast = -GeoMath.DistanceMeters(origin, new GeoCoordinate(origin.Latitude, p.Longitude));
        Assert.Equal(expectedNorth, n, 9);
        Assert.Equal(expectedEast, e, 9);
        Assert.Equal(120, n, 1);
        Assert.Equal(-80, e, 1);
    }

    [Fact]
    public void TileWidth_AtZ16_InPoland_IsStreetScale()
    {
        double w = WebMercator.TileWidthMeters(16, 50.06);
        Assert.InRange(w, 390, 395);
        // A projected z16 tile has about that width in the local frame.
        var t = new TileId(16, 36397, 22208);
        GeoBounds b = WebMercator.Bounds(t);
        var frame = new LocalTangentFrame(new GeoCoordinate((b.North + b.South) / 2, (b.West + b.East) / 2));
        frame.ToLocalMeters(b.North, b.West, out double we, out _);
        frame.ToLocalMeters(b.North, b.East, out double ee, out _);
        Assert.InRange(ee - we, 385, 400);
    }
}
