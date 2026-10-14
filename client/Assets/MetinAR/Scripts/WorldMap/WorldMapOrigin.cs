using System;
using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Geographic origin for the prototype world map, captured once from the player's first valid fix.
    /// Maps geo ↔ local meters: +X = east, +Z = north (1 Unity unit = 1 meter).
    /// There is no serialized/default origin: until <see cref="CaptureOrigin"/> runs,
    /// <see cref="HasOrigin"/> is false and geographic placement must wait.
    /// </summary>
    public sealed class WorldMapOrigin : MonoBehaviour
    {
        private GeoCoordinate _origin;
        private bool _hasOrigin;

        /// <summary>Raised once when the origin is captured.</summary>
        public event Action<GeoCoordinate> OriginCaptured;

        public bool HasOrigin => _hasOrigin;

        /// <summary>Origin coordinate. Only meaningful when <see cref="HasOrigin"/> is true.</summary>
        public GeoCoordinate Origin
        {
            get
            {
                if (!_hasOrigin)
                {
                    throw new InvalidOperationException("WorldMapOrigin has not been captured yet.");
                }

                return _origin;
            }
        }

        /// <summary>
        /// Captures the origin once. Later calls are ignored and return false.
        /// </summary>
        public bool CaptureOrigin(GeoCoordinate coordinate)
        {
            if (_hasOrigin)
            {
                return false;
            }

            _origin = coordinate;
            _hasOrigin = true;
            OriginCaptured?.Invoke(coordinate);
            return true;
        }

        /// <summary>
        /// Converts a geo coordinate to local Unity position (X=east, Z=north, Y given).
        /// Requires <see cref="HasOrigin"/>.
        /// </summary>
        public Vector3 WorldToLocal(GeoCoordinate coordinate, float y = 0f)
        {
            GetLocalMeters(coordinate, out double north, out double east);
            return new Vector3((float)east, y, (float)north);
        }

        /// <summary>
        /// Converts local meters (east, north) to a geo coordinate from the origin. Requires <see cref="HasOrigin"/>.
        /// </summary>
        public GeoCoordinate LocalToWorld(float eastMeters, float northMeters)
        {
            return GeoMath.OffsetMeters(Origin, northMeters, eastMeters);
        }

        public double DistanceMetersTo(GeoCoordinate coordinate)
        {
            return GeoMath.DistanceMeters(Origin, coordinate);
        }

        /// <summary>
        /// Local north/east offset of <paramref name="coordinate"/> relative to origin
        /// (signed distances along the meridian / parallel; adequate for prototype ranges).
        /// </summary>
        public void GetLocalMeters(GeoCoordinate coordinate, out double northMeters, out double eastMeters)
        {
            GeoCoordinate origin = Origin;
            var sameLon = new GeoCoordinate(coordinate.Latitude, origin.Longitude);
            double north = GeoMath.DistanceMeters(origin, sameLon);
            northMeters = coordinate.Latitude >= origin.Latitude ? north : -north;

            var sameLat = new GeoCoordinate(origin.Latitude, coordinate.Longitude);
            double east = GeoMath.DistanceMeters(origin, sameLat);
            eastMeters = coordinate.Longitude >= origin.Longitude ? east : -east;
        }
    }
}
