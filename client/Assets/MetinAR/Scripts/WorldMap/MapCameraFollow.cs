using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Angled map camera that follows the player and yaws with player heading.
    /// </summary>
    public sealed class MapCameraFollow : MonoBehaviour
    {
        [SerializeField]
        private Transform _target;

        [SerializeField]
        private PlayerMapAvatar _playerAvatar;

        [SerializeField]
        private Vector3 _offset = new Vector3(0f, 15f, -13f);

        [SerializeField]
        private float _followLerp = 8f;

        [SerializeField]
        private float _lookAtHeight = 1f;

        [SerializeField]
        private float _lookAheadMeters = 5f;

        public void Configure(Transform target, PlayerMapAvatar playerAvatar)
        {
            _target = target;
            _playerAvatar = playerAvatar;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            float yaw = _playerAvatar != null ? _playerAvatar.HeadingDegrees : 0f;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 desired = _target.position + yawRot * _offset;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-_followLerp * Time.deltaTime));

            Vector3 lookAt = _target.position + Vector3.up * _lookAtHeight + yawRot * Vector3.forward * _lookAheadMeters;
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
        }
    }
}
