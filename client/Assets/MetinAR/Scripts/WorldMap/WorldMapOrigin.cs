using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Fixed geographic origin for the prototype world map.
    /// Maps geo ↔ local meters: +X = east, +Z = north (1 Unity unit = 1 meter).
    /// </summary>
    public sealed class WorldMapOrigin : MonoBehaviour
    {
        [SerializeField]
        private double _originLatitude;

        [SerializeField]
        private double _originLongitude;

        private GeoCoordinate _origin;
        private bool _hasOrigin;

        public GeoCoordinate Origin => _origin;
        public bool HasOrigin => _hasOrigin;

        public void CaptureOrigin(GeoCoordinate coordinate)
        {
            _origin = coordinate;
            _originLatitude = coordinate.Latitude;
            _originLongitude = coordinate.Longitude;
            _hasOrigin = true;
        }

        public void EnsureOriginFromSerialized()
        {
            if (_hasOrigin)
            {
                return;
            }

            _origin = new GeoCoordinate(_originLatitude, _originLongitude);
            _hasOrigin = true;
        }

        /// <summary>
        /// Converts a geo coordinate to local Unity position (X=east, Z=north, Y unchanged).
        /// </summary>
        public Vector3 WorldToLocal(GeoCoordinate coordinate, float y = 0f)
        {
            EnsureOriginFromSerialized();
            double north = ApproximateNorthMeters(_origin, coordinate);
            double east = ApproximateEastMeters(_origin, coordinate);
            return new Vector3((float)east, y, (float)north);
        }

        /// <summary>
        /// Converts local meters (east, north) to a geo coordinate from the fixed origin.
        /// </summary>
        public GeoCoordinate LocalToWorld(float eastMeters, float northMeters)
        {
            EnsureOriginFromSerialized();
            return GeoMath.OffsetMeters(_origin, northMeters, eastMeters);
        }

        public double DistanceMetersTo(GeoCoordinate coordinate)
        {
            EnsureOriginFromSerialized();
            return GeoMath.DistanceMeters(_origin, coordinate);
        }

        /// <summary>
        /// Local north/east offset of <paramref name="coordinate"/> relative to origin.
        /// Uses OffsetMeters inverse via mid-point approximation suitable for prototype ranges.
        /// </summary>
        public void GetLocalMeters(GeoCoordinate coordinate, out double northMeters, out double eastMeters)
        {
            EnsureOriginFromSerialized();
            northMeters = ApproximateNorthMeters(_origin, coordinate);
            eastMeters = ApproximateEastMeters(_origin, coordinate);
        }

        private static double ApproximateNorthMeters(GeoCoordinate origin, GeoCoordinate target)
        {
            // Same longitude strip: north = signed distance along latitude.
            var sameLon = new GeoCoordinate(target.Latitude, origin.Longitude);
            double distance = GeoMath.DistanceMeters(origin, sameLon);
            return target.Latitude >= origin.Latitude ? distance : -distance;
        }

        private static double ApproximateEastMeters(GeoCoordinate origin, GeoCoordinate target)
        {
            var sameLat = new GeoCoordinate(origin.Latitude, target.Longitude);
            double distance = GeoMath.DistanceMeters(origin, sameLat);
            return target.Longitude >= origin.Longitude ? distance : -distance;
        }
    }
}
