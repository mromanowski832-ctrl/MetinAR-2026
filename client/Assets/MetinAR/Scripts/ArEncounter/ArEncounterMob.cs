using System.Collections;
using TMPro;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// The placed AR test monster (procedural placeholder spider): world-space name / level / HP bar
    /// above the model, turned toward the AR camera. #97: it walks to the player by itself (moved by
    /// <see cref="ArEncounterSceneController"/>); <see cref="SetGait"/> plays a procedural walk cycle while it moves. HP and death are driven by
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

        /// <summary>Shown in the HP line after death.</summary>
        public const string DefeatedLabel = "Pokonany";

        /// <summary>True after <see cref="ShowDefeated"/>.</summary>
        public bool IsDefeated { get; private set; }

        /// <summary>True once the body renderers were hidden after death (label stays).</summary>
        public bool IsBodyHidden { get; private set; }

        /// <summary>False while AR tracking is lost: the mob is not drawn with a stale camera pose (#31 v0 A31 P0-2).</summary>
        public bool IsTrackingVisible { get; private set; } = true;

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

        /// <summary>
        /// Hides (false) or restores (true) every renderer of the mob, nameplate included, without touching its pose or anchor.
        /// While ARCore is not tracking the camera pose freezes; content drawn then sticks to the screen and "moves with the
        /// phone". A body hidden after death stays hidden.
        /// </summary>
        public void SetTrackingVisible(bool visible)
        {
            if (IsTrackingVisible == visible)
            {
                return;
            }

            IsTrackingVisible = visible;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                bool isLabel = _labelRoot != null && r.transform.IsChildOf(_labelRoot);
                r.enabled = visible && (isLabel || !IsBodyHidden);
            }
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

        private struct GaitPart
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public Quaternion BaseRotation;
            public Vector3 Hip;
            public bool Left;
            public int Index;
        }

        private GaitPart[] _gaitParts;
        private Transform _gaitModel;
        private Vector3 _gaitModelBase;
        private float _gaitWeight;

        /// <summary>Current walk-cycle weight (0 = standing, 1 = full gait at cruise speed).</summary>
        public float GaitWeight => _gaitWeight;

        /// <summary>
        /// #97 rule 5: procedural walk cycle (no clips on the placeholder spider). <paramref name="travelledMeters"/> drives
        /// the phase (no foot skating), <paramref name="speed01"/> fades it in/out. Visual only: the mob root (hit geometry)
        /// is not touched. Legs are the "Leg{L|R}{0-3}_{Upper|Lower}" children of "Model" built by the prototype scene builder.
        /// </summary>
        public void SetGait(double travelledMeters, float speed01)
        {
            if (_gaitParts == null)
            {
                CacheGaitParts();
            }

            float target = IsDefeated ? 0f : Mathf.Clamp01(speed01);
            _gaitWeight = Mathf.MoveTowards(_gaitWeight, target, Time.deltaTime * 6f);
            double phase = ArMobGait.Phase(travelledMeters);
            if (_gaitModel != null)
            {
                _gaitModel.localPosition = _gaitModelBase + Vector3.up * (float)ArMobGait.Bob(phase, _gaitWeight);
            }

            foreach (GaitPart part in _gaitParts)
            {
                if (part.Transform == null)
                {
                    continue;
                }

                Quaternion swing = Quaternion.AngleAxis((float)ArMobGait.LegSwing(phase, part.Left, part.Index, _gaitWeight), Vector3.up);
                float lift = (float)ArMobGait.LegLift(phase, part.Left, part.Index, _gaitWeight);
                part.Transform.localPosition = part.Hip + swing * (part.BasePosition - part.Hip) + Vector3.up * lift;
                part.Transform.localRotation = swing * part.BaseRotation;
            }
        }

        private void CacheGaitParts()
        {
            var parts = new System.Collections.Generic.List<GaitPart>();
            _gaitModel = transform.Find("Model");
            if (_gaitModel != null)
            {
                _gaitModelBase = _gaitModel.localPosition;
                for (int i = 0; i < 4; i++)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        bool left = s == 0;
                        string prefix = "Leg" + (left ? "L" : "R") + i;
                        Transform upper = _gaitModel.Find(prefix + "_Upper");
                        Transform lower = _gaitModel.Find(prefix + "_Lower");
                        if (upper == null)
                        {
                            continue;
                        }

                        // Capsule segment: centre between its joints, local up along the segment, height 2·scale.y incl. caps.
                        float half = Mathf.Max(0f, upper.localScale.y - upper.localScale.x * 0.5f);
                        Vector3 hip = upper.localPosition - upper.localRotation * Vector3.up * half;
                        parts.Add(new GaitPart { Transform = upper, BasePosition = upper.localPosition, BaseRotation = upper.localRotation, Hip = hip, Left = left, Index = i });
                        if (lower != null)
                        {
                            parts.Add(new GaitPart { Transform = lower, BasePosition = lower.localPosition, BaseRotation = lower.localRotation, Hip = hip, Left = left, Index = i });
                        }
                    }
                }
            }

            _gaitParts = parts.ToArray();
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
