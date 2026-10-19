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

        /// <summary>
        /// Shared runtime path for keyboard and tests. input.x = strafe (right +), input.y = forward (+).
        /// Moves the mock provider along current heading. No-op in release builds.
        /// </summary>
        public void ApplyMoveInput(Vector2 input, float deltaSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_locationService == null || _locationService.MockProvider == null)
            {
                return;
            }

            if (Mathf.Abs(input.x) < 0.01f && Mathf.Abs(input.y) < 0.01f)
            {
                return;
            }

            float speed = _moveMetersPerSecond * deltaSeconds;
            float headingRad = _playerAvatar != null ? _playerAvatar.HeadingDegrees * Mathf.Deg2Rad : 0f;
            // Heading 0 = north (+Z), clockwise positive. Forward along heading; strafe to the right.
            double north = (input.y * Mathf.Cos(headingRad) - input.x * Mathf.Sin(headingRad)) * speed;
            double east = (input.y * Mathf.Sin(headingRad) + input.x * Mathf.Cos(headingRad)) * speed;
            _locationService.MockProvider.MoveMeters(north, east);
#endif
        }

        /// <summary>
        /// Shared runtime path for Q/E and tests. turn: -1 left, +1 right. No-op in release builds.
        /// </summary>
        public void ApplyTurnInput(float turn, float deltaSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_playerAvatar == null || Mathf.Abs(turn) < 0.01f)
            {
                return;
            }

            _playerAvatar.HeadingDegrees =
                Mathf.Repeat(_playerAvatar.HeadingDegrees + turn * _turnDegreesPerSecond * deltaSeconds, 360f);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void TickMockMovement()
        {
            float forward = 0f;
            float strafe = 0f;
            float turn = 0f;
            ReadInput(ref forward, ref strafe, ref turn);
            float dt = Time.deltaTime;
            ApplyTurnInput(turn, dt);
            ApplyMoveInput(new Vector2(strafe, forward), dt);
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
