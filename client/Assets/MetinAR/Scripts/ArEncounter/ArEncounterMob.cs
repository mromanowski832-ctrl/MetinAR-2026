using System.Collections;
using TMPro;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// The placed AR test monster (procedural placeholder spider): world-space name / level / HP bar
    /// above the model, turned toward the AR camera. Stationary. HP and death are driven by
    /// <see cref="ArEncounterCombat"/> (METINAR-020); no mob attacks (S3) or drops (S4) yet.
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
        private float _bodyHideDelaySeconds = 1f;

        private Camera _camera;

        /// <summary>Shown in the HP line after death.</summary>
        public const string DefeatedLabel = "Pokonany";

        /// <summary>True after <see cref="ShowDefeated"/>.</summary>
        public bool IsDefeated { get; private set; }

        /// <summary>True once the body renderers were hidden after death (label stays).</summary>
        public bool IsBodyHidden { get; private set; }

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

        /// <summary>Updates the HP line and bar (METINAR-020).</summary>
        public void ShowHp(int currentHp, int maxHp)
        {
            if (_hpText != null)
            {
                _hpText.text = "HP " + currentHp + "/" + maxHp;
            }

            if (_hpFillPivot != null)
            {
                Vector3 s = _hpFillPivot.localScale;
                float fraction = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
                _hpFillPivot.localScale = new Vector3(fraction, s.y, s.z);
            }
        }

        /// <summary>
        /// Death: HP bar empty, label "Pokonany", body hidden after ~1 s (COMBAT_PHYSICS.md §3: Death → 1 s dissolve;
        /// the Death clip and dissolve shader come with the production spider asset).
        /// </summary>
        public void ShowDefeated()
        {
            if (IsDefeated)
            {
                return;
            }

            IsDefeated = true;
            if (_hpFillPivot != null)
            {
                Vector3 s = _hpFillPivot.localScale;
                _hpFillPivot.localScale = new Vector3(0f, s.y, s.z);
            }

            if (_hpText != null)
            {
                _hpText.text = DefeatedLabel;
            }

            if (isActiveAndEnabled)
            {
                StartCoroutine(HideBodyAfterDelay());
            }
            else
            {
                HideBody();
            }
        }

        private IEnumerator HideBodyAfterDelay()
        {
            yield return new WaitForSeconds(_bodyHideDelaySeconds);
            HideBody();
        }

        private void HideBody()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (_labelRoot != null && r.transform.IsChildOf(_labelRoot))
                {
                    continue;
                }

                r.enabled = false;
            }

            IsBodyHidden = true;
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
