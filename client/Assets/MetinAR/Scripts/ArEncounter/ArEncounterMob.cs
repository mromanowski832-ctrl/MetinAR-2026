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

        /// <summary>Top of the procedural spider body in its unscaled local space (metres).</summary>
        public const float MobTopLocalHeight = 0.6f;

        private Camera _camera;
        private bool _hasLabelAnchor;
        private Vector3 _labelLocalAnchor;
        private Vector3 _labelLocalScale = Vector3.one;

        /// <summary>World scale factor of the nameplate applied last frame (1 = authored size).</summary>
        public float LabelScaleFactor { get; private set; } = 1f;

        /// <summary>The nameplate was lowered this frame to stay out of the top HUD band.</summary>
        public bool LabelClampedBelowHud { get; private set; }

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

            ClampLabel();
            FaceCamera();
        }

        private void LateUpdate()
        {
            ClampLabel();
            FaceCamera();
        }

        /// <summary>
        /// Keeps the world-space nameplate readable but small (#31 A31 retest: huge up close): constant maximum
        /// angular size when near, authored size when far, and below the top HUD band without covering the mob.
        /// </summary>
        public void ClampLabel()
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (_labelRoot == null || cam == null)
            {
                return;
            }

            if (!_hasLabelAnchor)
            {
                _labelLocalAnchor = _labelRoot.localPosition;
                _labelLocalScale = _labelRoot.localScale;
                _hasLabelAnchor = true;
            }

            Transform parent = _labelRoot.parent;
            Vector3 anchor = parent != null ? parent.TransformPoint(_labelLocalAnchor) : _labelLocalAnchor;
            float distance = Vector3.Distance(cam.transform.position, anchor);
            float factor = (float)ArEncounterFraming.NameplateScale(distance);
            float parentScale = parent != null ? Mathf.Max(1e-4f, parent.lossyScale.x) : 1f;
            _labelRoot.localScale = _labelLocalScale * (factor / parentScale);
            LabelScaleFactor = factor;

            Vector3 position = anchor;
            LabelClampedBelowHud = false;
            Vector3 vp = cam.WorldToViewportPoint(anchor);
            if (vp.z > cam.nearClipPlane)
            {
                Vector3 top = transform.TransformPoint(new Vector3(0f, MobTopLocalHeight, 0f));
                Vector3 topVp = cam.WorldToViewportPoint(top);
                double y = ArEncounterFraming.ClampNameplateViewportY(vp.y, topVp.z > 0f ? topVp.y : double.NaN);
                if (y < vp.y - 1e-4)
                {
                    position = cam.ViewportToWorldPoint(new Vector3(vp.x, (float)y, vp.z));
                    LabelClampedBelowHud = true;
                }
            }

            _labelRoot.position = position;
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
