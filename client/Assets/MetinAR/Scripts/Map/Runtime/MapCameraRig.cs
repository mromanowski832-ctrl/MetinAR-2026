using MetinAR.WorldMap;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// Angled Pokémon-GO style follow camera for the OSM POC (separate from the 004A
    /// <see cref="MapCameraFollow"/>, which stays untouched). Orbits the target: yaw by horizontal drag /
    /// Q-E, pitch by two-finger vertical drag / R-F / right-mouse drag, distance by pinch / scroll.
    /// Optionally follows the avatar heading. Everything also reachable through the public API (tests).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class MapCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerMapAvatar _headingSource;
        [SerializeField] private bool _followHeading;
        [SerializeField] private float _yawDegrees;
        [SerializeField, Range(10f, 89f)] private float _pitchDegrees = 50f;
        [SerializeField] private float _distanceMeters = 35f;
        [SerializeField] private float _minPitch = 30f;
        [SerializeField] private float _maxPitch = 75f;
        [SerializeField] private float _minDistance = 12f;
        [SerializeField] private float _maxDistance = 80f;
        [SerializeField] private float _lookAtHeight = 1f;
        [SerializeField] private float _dragDegreesPerScreen = 180f;
        [SerializeField] private float _keyDegreesPerSecond = 90f;
        [SerializeField] private float _smoothing = 12f;

        private Vector3 _smoothedTarget;
        private bool _hasSmoothed;
        private float _lastPinchDistance = -1f;
        private Vector2 _lastTwoFingerCenter;

        public float YawDegrees => _yawDegrees;
        public float PitchDegrees => _pitchDegrees;
        public float DistanceMeters => _distanceMeters;
        public Transform Target => _target;

        public void Configure(Transform target, PlayerMapAvatar headingSource)
        {
            _target = target;
            _headingSource = headingSource;
        }

        public void Rotate(float deltaYawDegrees)
        {
            _yawDegrees = Mathf.Repeat(_yawDegrees + deltaYawDegrees, 360f);
        }

        public void Tilt(float deltaPitchDegrees)
        {
            _pitchDegrees = Mathf.Clamp(_pitchDegrees + deltaPitchDegrees, _minPitch, _maxPitch);
        }

        public void Zoom(float factor)
        {
            if (factor > 0f)
            {
                _distanceMeters = Mathf.Clamp(_distanceMeters * factor, _minDistance, _maxDistance);
            }
        }

        /// <summary>Applies the current pose immediately (no smoothing).</summary>
        public void Snap()
        {
            _hasSmoothed = false;
            Apply(1f);
        }

        private void LateUpdate()
        {
            ReadInput(Time.unscaledDeltaTime);
            Apply(1f - Mathf.Exp(-_smoothing * Time.unscaledDeltaTime));
        }

        private void Apply(float t)
        {
            if (_target == null)
            {
                return;
            }

            Vector3 focus = _target.position + Vector3.up * _lookAtHeight;
            _smoothedTarget = _hasSmoothed ? Vector3.Lerp(_smoothedTarget, focus, t) : focus;
            _hasSmoothed = true;
            float yaw = _yawDegrees + (_followHeading && _headingSource != null ? _headingSource.HeadingDegrees : 0f);
            Quaternion rotation = Quaternion.Euler(_pitchDegrees, yaw, 0f);
            transform.SetPositionAndRotation(_smoothedTarget - rotation * Vector3.forward * _distanceMeters, rotation);
        }

        private void ReadInput(float dt)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.isPressed) Rotate(-_keyDegreesPerSecond * dt);
                if (kb.eKey.isPressed) Rotate(_keyDegreesPerSecond * dt);
                if (kb.rKey.isPressed) Tilt(_keyDegreesPerSecond * 0.5f * dt);
                if (kb.fKey.isPressed) Tilt(-_keyDegreesPerSecond * 0.5f * dt);
                if (kb.zKey.isPressed) Zoom(1f - dt);
                if (kb.xKey.isPressed) Zoom(1f + dt);
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && !PointerOverUi(-1))
            {
                Vector2 delta = mouse.delta.ReadValue();
                float perPixel = _dragDegreesPerScreen / Mathf.Max(1f, Screen.width);
                if (mouse.leftButton.isPressed)
                {
                    Rotate(delta.x * perPixel);
                }

                if (mouse.rightButton.isPressed)
                {
                    Tilt(-delta.y * perPixel);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    Zoom(scroll > 0 ? 0.9f : 1.1f);
                }
            }

            Touchscreen touch = Touchscreen.current;
            if (touch == null)
            {
                return;
            }

            int active = 0;
            UnityEngine.InputSystem.Controls.TouchControl a = null, b = null;
            foreach (UnityEngine.InputSystem.Controls.TouchControl t in touch.touches)
            {
                if (!t.press.isPressed)
                {
                    continue;
                }

                if (active == 0) a = t; else if (active == 1) b = t;
                active++;
            }

            if (active == 1 && a != null && !PointerOverUi(a.touchId.ReadValue()))
            {
                Rotate(a.delta.ReadValue().x * _dragDegreesPerScreen / Mathf.Max(1f, Screen.width));
                _lastPinchDistance = -1f;
            }
            else if (active >= 2 && a != null && b != null)
            {
                Vector2 pa = a.position.ReadValue(), pb = b.position.ReadValue();
                float distance = Vector2.Distance(pa, pb);
                Vector2 center = (pa + pb) * 0.5f;
                if (_lastPinchDistance > 0f && distance > 1f)
                {
                    Zoom(_lastPinchDistance / distance);
                    Tilt(-(center.y - _lastTwoFingerCenter.y) * _dragDegreesPerScreen * 0.5f / Mathf.Max(1f, Screen.height));
                }

                _lastPinchDistance = distance;
                _lastTwoFingerCenter = center;
            }
            else
            {
                _lastPinchDistance = -1f;
            }
#endif
        }

        private static bool PointerOverUi(int pointerId)
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }
    }
}
