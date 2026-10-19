using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Runtime safety net: fills obvious missing refs when the scene is opened without the builder.
    /// Does not invent a full HUD — only wires FindObjectOfType links when null.
    /// </summary>
    public sealed class WorldMapPrototypeBootstrap : MonoBehaviour
    {
        [SerializeField]
        private PlayerLocationService _locationService;

        [SerializeField]
        private WorldMapOrigin _origin;

        [SerializeField]
        private PlayerMapAvatar _playerAvatar;

        [SerializeField]
        private MapCameraFollow _cameraFollow;

        [SerializeField]
        private EditorMockMovement _mockMovement;

        [SerializeField]
        private WorldMapPrototypeController _controller;

        private void Awake()
        {
            if (_locationService == null)
            {
                _locationService = FindFirstObjectByType<PlayerLocationService>();
            }

            if (_origin == null)
            {
                _origin = FindFirstObjectByType<WorldMapOrigin>();
            }

            if (_playerAvatar == null)
            {
                _playerAvatar = FindFirstObjectByType<PlayerMapAvatar>();
            }

            if (_cameraFollow == null)
            {
                _cameraFollow = FindFirstObjectByType<MapCameraFollow>();
            }

            if (_mockMovement == null)
            {
                _mockMovement = FindFirstObjectByType<EditorMockMovement>();
            }

            if (_controller == null)
            {
                _controller = FindFirstObjectByType<WorldMapPrototypeController>();
            }

            if (_playerAvatar != null && _locationService != null && _origin != null)
            {
                _playerAvatar.Configure(_locationService, _origin);
            }

            if (_cameraFollow != null && _playerAvatar != null)
            {
                _cameraFollow.Configure(_playerAvatar.transform, _playerAvatar);
            }

            if (_mockMovement != null && _locationService != null && _playerAvatar != null)
            {
                _mockMovement.Configure(_locationService, _playerAvatar);
            }
        }
    }
}
