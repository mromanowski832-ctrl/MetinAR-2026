using MetinAR.Location;
using TMPro;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Places a transform from a geographic coordinate via <see cref="WorldMapOrigin"/>.
    /// Optional world-space name/distance label.
    /// </summary>
    public sealed class WorldMapEntity : MonoBehaviour
    {
        [SerializeField]
        private WorldMapOrigin _origin;

        [SerializeField]
        private double _latitude;

        [SerializeField]
        private double _longitude;

        [SerializeField]
        private float _groundY;

        [SerializeField]
        private TextMeshPro _worldLabel;

        [SerializeField]
        private string _displayName = "Entity";

        [SerializeField]
        private bool _showDistanceInLabel = true;

        [SerializeField]
        private Transform _distanceReference;

        private GeoCoordinate _coordinate;
        private bool _coordinateReady;

        public GeoCoordinate Coordinate
        {
            get
            {
                EnsureCoordinate();
                return _coordinate;
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        public void Configure(
            WorldMapOrigin origin,
            GeoCoordinate coordinate,
            string displayName,
            TextMeshPro worldLabel = null,
            Transform distanceReference = null,
            bool showDistance = true)
        {
            _origin = origin;
            _coordinate = coordinate;
            _latitude = coordinate.Latitude;
            _longitude = coordinate.Longitude;
            _coordinateReady = true;
            _displayName = displayName;
            _worldLabel = worldLabel;
            _distanceReference = distanceReference;
            _showDistanceInLabel = showDistance;
            ApplyTransform();
            RefreshLabel();
        }

        public void SetCoordinate(GeoCoordinate coordinate)
        {
            _coordinate = coordinate;
            _latitude = coordinate.Latitude;
            _longitude = coordinate.Longitude;
            _coordinateReady = true;
            ApplyTransform();
            RefreshLabel();
        }

        private void Awake()
        {
            EnsureCoordinate();
        }

        private void LateUpdate()
        {
            ApplyTransform();
            RefreshLabel();
        }

        private void EnsureCoordinate()
        {
            if (_coordinateReady)
            {
                return;
            }

            _coordinate = new GeoCoordinate(_latitude, _longitude);
            _coordinateReady = true;
        }

        private void ApplyTransform()
        {
            if (_origin == null)
            {
                return;
            }

            EnsureCoordinate();
            Vector3 local = _origin.WorldToLocal(_coordinate, _groundY);
            transform.position = local;
        }

        private void RefreshLabel()
        {
            if (_worldLabel == null)
            {
                return;
            }

            if (_showDistanceInLabel && _distanceReference != null)
            {
                float meters = Vector3.Distance(transform.position, _distanceReference.position);
                _worldLabel.text = _displayName + "\n" + meters.ToString("F0") + " m";
            }
            else
            {
                _worldLabel.text = _displayName;
            }

            if (Camera.main != null)
            {
                _worldLabel.transform.rotation = Quaternion.LookRotation(
                    _worldLabel.transform.position - Camera.main.transform.position);
            }
        }
    }
}
