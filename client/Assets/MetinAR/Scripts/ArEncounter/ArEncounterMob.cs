using TMPro;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// The placed AR test monster (procedural placeholder spider): world-space name / level / HP bar
    /// above the model, turned toward the AR camera. Stationary; no combat, damage or drops.
    /// METINAR-007: a local-space hit box for the hand-tracked sword and a short visual pulse on a sword hit
    /// (prototype feedback only; HP is unchanged — no damage formula yet).
    /// </summary>
    public sealed class ArEncounterMob : MonoBehaviour
    {
        [SerializeField]
        private Transform _labelRoot;

        [SerializeField]
        private TextMeshPro _nameText;

        [SerializeField]
        private TextMeshPro _levelText;

        [SerializeField]
        private TextMeshPro _hpText;

        [SerializeField]
        private Transform _hpFillPivot;

        [SerializeField]
        private Vector3 _hitVolumeCenter = new Vector3(0f, 0.3f, 0.08f);

        [SerializeField]
        private Vector3 _hitVolumeHalfExtents = new Vector3(0.66f, 0.3f, 0.56f);

        private Camera _camera;
        private Transform _model;
        private Vector3 _modelBaseScale = Vector3.one;
        private float _pulseUntil;

        public const float HitPulseSeconds = 0.18f;

        /// <summary>Sword hit box centre in this transform's local space (metres).</summary>
        public Vector3 HitVolumeCenter => _hitVolumeCenter;

        public Vector3 HitVolumeHalfExtents => _hitVolumeHalfExtents;

        /// <summary>Number of valid sword hits registered on this mob (test-observable; no damage applied).</summary>
        public int SwordHitCount { get; private set; }

        public bool IsHitPulseActive => Time.time < _pulseUntil;

        public ArEncounterRequest Request { get; private set; }

        public string NameLabel => _nameText != null ? _nameText.text : null;

        public string LevelLabel => _levelText != null ? _levelText.text : null;

        public string HpLabel => _hpText != null ? _hpText.text : null;

        public float HpFillFraction => _hpFillPivot != null ? _hpFillPivot.localScale.x : -1f;

        public Transform LabelRoot => _labelRoot;

        public void ConfigureVisuals(Transform labelRoot, TextMeshPro nameText, TextMeshPro levelText, TextMeshPro hpText, Transform hpFillPivot)
        {
            _labelRoot = labelRoot;
            _nameText = nameText;
            _levelText = levelText;
            _hpText = hpText;
            _hpFillPivot = hpFillPivot;
        }

        public void Configure(ArEncounterRequest request, Camera viewCamera)
        {
            Request = request;
            _camera = viewCamera;
            if (request == null)
            {
                return;
            }

            if (_nameText != null)
            {
                _nameText.text = request.MobName;
            }

            if (_levelText != null)
            {
                _levelText.text = request.LevelLabel;
            }

            if (_hpText != null)
            {
                _hpText.text = request.HpLabel;
            }

            if (_hpFillPivot != null)
            {
                Vector3 s = _hpFillPivot.localScale;
                _hpFillPivot.localScale = new Vector3(Mathf.Clamp01(request.HpFraction), s.y, s.z);
            }

            FaceCamera();
        }

        private void Awake()
        {
            _model = transform.Find("Model");
            if (_model != null)
            {
                _modelBaseScale = _model.localScale;
            }
        }

        /// <summary>Registers a valid sword hit: counter + brief scale pulse. HP and stats are not changed.</summary>
        public void RegisterSwordHit()
        {
            SwordHitCount++;
            _pulseUntil = Time.time + HitPulseSeconds;
        }

        private void LateUpdate()
        {
            FaceCamera();
            if (_model != null)
            {
                float remaining = _pulseUntil - Time.time;
                float k = remaining > 0f ? Mathf.Sin(remaining / HitPulseSeconds * Mathf.PI) * 0.12f : 0f;
                _model.localScale = _modelBaseScale * (1f + k);
            }
        }

        private void FaceCamera()
        {
            if (_labelRoot == null)
            {
                return;
            }

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 away = _labelRoot.position - cam.transform.position;
            if (away.sqrMagnitude > 1e-6f)
            {
                _labelRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }
    }
}
