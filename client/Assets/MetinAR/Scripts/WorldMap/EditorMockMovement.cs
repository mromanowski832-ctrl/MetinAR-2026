using MetinAR.Location;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Editor / development-only mock movement. WASD/arrows → MoveMeters; Q/E → heading.
    /// Compiles to a no-op MonoBehaviour in release player builds without DEVELOPMENT_BUILD.
    /// </summary>
    public sealed class EditorMockMovement : MonoBehaviour
    {
        [SerializeField]
        private PlayerLocationService _locationService;

        [SerializeField]
        private PlayerMapAvatar _playerAvatar;

        [SerializeField]
        private float _moveMetersPerSecond = 8f;

        [SerializeField]
        private float _turnDegreesPerSecond = 90f;

        public void Configure(PlayerLocationService locationService, PlayerMapAvatar playerAvatar)
        {
            _locationService = locationService;
            _playerAvatar = playerAvatar;
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TickMockMovement();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void TickMockMovement()
        {
            if (_locationService == null)
            {
                return;
            }

            MockLocationProvider mock = _locationService.MockProvider;
            if (mock == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            float speed = _moveMetersPerSecond * dt;

            float forward = 0f;
            float strafe = 0f;
            float turn = 0f;
            ReadInput(ref forward, ref strafe, ref turn);

            if (_playerAvatar != null && Mathf.Abs(turn) > 0.01f)
            {
                _playerAvatar.HeadingDegrees =
                    Mathf.Repeat(_playerAvatar.HeadingDegrees + turn * _turnDegreesPerSecond * dt, 360f);
            }

            if (Mathf.Abs(forward) < 0.01f && Mathf.Abs(strafe) < 0.01f)
            {
                return;
            }

            float headingRad = 0f;
            if (_playerAvatar != null)
            {
                headingRad = _playerAvatar.HeadingDegrees * Mathf.Deg2Rad;
            }

            // Heading 0 = north (+Z). Forward along heading; strafe to the right.
            double north = forward * Mathf.Cos(headingRad) * speed + strafe * Mathf.Sin(headingRad) * speed;
            double east = forward * Mathf.Sin(headingRad) * speed + strafe * Mathf.Cos(headingRad) * speed;
            mock.MoveMeters(north, east);
        }

        private static void ReadInput(ref float forward, ref float strafe, ref float turn)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) forward += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) forward -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) strafe += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) strafe -= 1f;
            if (kb.qKey.isPressed) turn -= 1f;
            if (kb.eKey.isPressed) turn += 1f;
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) forward += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) forward -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) strafe += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) strafe -= 1f;
            if (Input.GetKey(KeyCode.Q)) turn -= 1f;
            if (Input.GetKey(KeyCode.E)) turn += 1f;
#endif
        }
#endif
    }
}
