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
        private bool _hasCoordinate;

        /// <summary>True once a geographic coordinate was assigned at runtime.</summary>
        public bool HasCoordinate => _hasCoordinate;

        /// <summary>True when both the coordinate and the shared origin are available.</summary>
        public bool IsPlaced => _hasCoordinate && _origin != null && _origin.HasOrigin;

        public GeoCoordinate Coordinate => _coordinate;

        public string LabelText => _worldLabel != null ? _worldLabel.text : null;

        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>
        /// Wires references only. The coordinate is assigned later via <see cref="SetCoordinate"/>
        /// (by <see cref="PrototypeWorldPlacement"/> after the first fix).
        /// </summary>
        public void Configure(
            WorldMapOrigin origin,
            string displayName,
            TextMeshPro worldLabel = null,
            Transform distanceReference = null,
            bool showDistance = true)
        {
            _origin = origin;
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
            _hasCoordinate = true;
            ApplyTransform();
            RefreshLabel();
        }

        private void LateUpdate()
        {
            ApplyTransform();
            RefreshLabel();
        }

        private void ApplyTransform()
        {
            if (!IsPlaced)
            {
                return;
            }

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
