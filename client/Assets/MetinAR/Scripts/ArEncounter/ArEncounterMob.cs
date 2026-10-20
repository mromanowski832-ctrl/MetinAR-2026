using TMPro;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// The placed AR test monster (procedural placeholder spider): world-space name / level / HP bar
    /// above the model, turned toward the AR camera. Stationary; no combat, damage or drops.
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

        private Camera _camera;

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

        private void LateUpdate()
        {
            FaceCamera();
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
