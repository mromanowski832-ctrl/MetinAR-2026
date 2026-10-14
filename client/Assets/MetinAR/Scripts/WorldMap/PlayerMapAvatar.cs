using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Origin capture is owned by <see cref="PrototypeWorldPlacement"/>; the avatar only follows
    /// fixes once <see cref="WorldMapOrigin.HasOrigin"/> is true.
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

        private void Update()
        {
            ApplyFromFix();
            ApplyHeading();
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
            // Unity yaw: 0 looks +Z (north), positive yaw is clockwise seen from above = compass heading.
            target.rotation = Quaternion.Euler(0f, _headingDegrees, 0f);
        }
    }
}
