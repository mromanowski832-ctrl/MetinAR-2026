using System;
using MetinAR.Location;

namespace MetinAR.Map
{
    /// <summary>
    /// Pure replica of the <c>WorldMapOrigin</c> local-meter contract (METINAR-004B):
    /// +X = east, +Z = north, 1 unit = 1 m, measured as signed great-circle distances along the origin's
    /// meridian (north) and parallel (east) with <see cref="GeoMath.DistanceMeters"/>.
    /// Map geometry projected with this frame lines up exactly with entities placed by
    /// <c>WorldMapOrigin.WorldToLocal</c> (verified by the Play Mode smoke test).
    /// Thread-safe (immutable), so it can run on worker threads.
    /// </summary>
    public sealed class LocalTangentFrame
    {
        public LocalTangentFrame(GeoCoordinate origin)
        {
            Origin = origin;
        }

        public GeoCoordinate Origin { get; }

        public void ToLocalMeters(double latitude, double longitude, out double eastMeters, out double northMeters)
        {
            double lat = Math.Max(-90.0, Math.Min(90.0, latitude));
            double lon = Math.Max(-180.0, Math.Min(180.0, longitude));
            var sameLon = new GeoCoordinate(lat, Origin.Longitude);
            double north = GeoMath.DistanceMeters(Origin, sameLon);
            northMeters = lat >= Origin.Latitude ? north : -north;

            var sameLat = new GeoCoordinate(Origin.Latitude, lon);
            double east = GeoMath.DistanceMeters(Origin, sameLat);
            eastMeters = lon >= Origin.Longitude ? east : -east;
        }

        public void ToLocalMeters(GeoCoordinate coordinate, out double eastMeters, out double northMeters)
        {
            ToLocalMeters(coordinate.Latitude, coordinate.Longitude, out eastMeters, out northMeters);
        }

        /// <summary>Inverse used for probes and tests (same as WorldMapOrigin.LocalToWorld).</summary>
        public GeoCoordinate ToGeo(double eastMeters, double northMeters)
        {
            return GeoMath.OffsetMeters(Origin, northMeters, eastMeters);
        }
    }
}
