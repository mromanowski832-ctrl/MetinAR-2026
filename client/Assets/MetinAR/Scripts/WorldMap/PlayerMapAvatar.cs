using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Moves the player avatar from <see cref="PlayerLocationService"/> fixes
    /// relative to a fixed <see cref="WorldMapOrigin"/>. Rotates by heading (degrees, 0 = north).
    /// </summary>
    public sealed class PlayerMapAvatar : MonoBehaviour
    {
        [SerializeField]
        private PlayerLocationService _locationService;

        [SerializeField]
        private WorldMapOrigin _origin;

        [SerializeField]
        private Transform _modelRoot;

        [SerializeField]
        private float _headingDegrees;

        [SerializeField]
        private float _groundY;

        public float HeadingDegrees
        {
            get => _headingDegrees;
            set => _headingDegrees = value;
        }

        public PlayerLocationService LocationService => _locationService;
        public WorldMapOrigin Origin => _origin;

        public void Configure(PlayerLocationService locationService, WorldMapOrigin origin, Transform modelRoot = null)
        {
            _locationService = locationService;
            _origin = origin;
            if (modelRoot != null)
            {
                _modelRoot = modelRoot;
            }
        }

        private void Start()
        {
            TryCaptureOrigin();
            ApplyFromFix();
        }

        private void Update()
        {
            TryCaptureOrigin();
            ApplyFromFix();
            ApplyHeading();
        }

        private void TryCaptureOrigin()
        {
            if (_origin == null || _locationService == null)
            {
                return;
            }

            if (_origin.HasOrigin)
            {
                return;
            }

            if (_locationService.TryGetLatestFix(out LocationFix fix))
            {
                _origin.CaptureOrigin(fix.Coordinate);
            }
        }

        private void ApplyFromFix()
        {
            if (_origin == null || _locationService == null || !_origin.HasOrigin)
            {
                return;
            }

            if (!_locationService.TryGetLatestFix(out LocationFix fix))
            {
                return;
            }

            transform.position = _origin.WorldToLocal(fix.Coordinate, _groundY);
        }

        private void ApplyHeading()
        {
            Transform target = _modelRoot != null ? _modelRoot : transform;
            // Unity yaw: 0 looks +Z (north). Heading degrees clockwise from north → yaw = -heading.
            target.rotation = Quaternion.Euler(0f, -_headingDegrees, 0f);
        }
    }
}
