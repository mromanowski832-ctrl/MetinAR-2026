using System;
using MetinAR.ArEncounter;
using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace MetinAR.HandTracking
{
    /// <summary>Test-observable data of one valid sword hit.</summary>
    public struct ArSwordHitInfo
    {
        public int HitNumber;
        public float Time;
        public float TipSpeed;
        public Vector3 TipPosition;
        public string MobName;
    }

    /// <summary>
    /// METINAR-007: places the procedural sword in the player's REAL tracked hand (not camera-fixed): each hand
    /// observation is lifted into the AR camera space (pure <see cref="HandPoseEstimator"/>), moved into AR world space
    /// with the camera pose of that camera frame, smoothed (<see cref="HandPoseSmoother"/>) and rendered. The blade is
    /// tested against the placed mob's hit box every frame (swept), and <see cref="SwordHitGate"/> decides whether the
    /// contact is a valid swing hit; valid hits raise <see cref="SwordHit"/>. No damage formula (prototype).
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class ArHandSword : MonoBehaviour
    {
        public const string DefaultHitToast = "Trafienie mieczem! (prototyp – bez obrażeń)";

        [SerializeField]
        private ArHandTracker _tracker;

        [SerializeField]
        private ArEncounterSceneController _encounter;

        [SerializeField]
        private Transform _swordRoot;

        [SerializeField]
        private Transform _bladeBase;

        [SerializeField]
        private Transform _bladeTip;

        [SerializeField]
        private float _bladeRadius = 0.025f;

        [SerializeField]
        private SwordGripMode _gripMode = SwordGripMode.Fist;

        [SerializeField]
        private float _observationTimeoutSeconds = 0.5f;

        [SerializeField]
        private SwordSwingSettings _swing = SwordSwingSettings.CreatePrototypeDefaults();

        private readonly HandPoseEstimator _estimator = new HandPoseEstimator();
        private readonly HandPoseSmoother _smoother = new HandPoseSmoother();
        private SwordHitGate _gate;
        private int _lastSequence = -1;
        private bool _rawValid;
        private float _rawTime;
        private Vector3 _previousBase;
        private Vector3 _previousTip;
        private bool _hasPrevious;

        /// <summary>Raised once per valid swing hit on the placed mob.</summary>
        public event Action<ArSwordHitInfo> SwordHit;

        public int HitCount { get; private set; }

        public ArSwordHitInfo? LastHit { get; private set; }

        public SwordHitDecision LastDecision { get; private set; }

        /// <summary>Frames in which the blade touched the mob's hit box (valid or not).</summary>
        public int ContactFrames { get; private set; }

        public float TipSpeed => _gate != null ? _gate.TipSpeed : 0f;

        public bool IsSwordVisible => _swordRoot != null && _swordRoot.gameObject.activeSelf;

        public bool IsHolding { get; private set; }

        public Transform SwordRoot => _swordRoot;

        public Transform BladeBase => _bladeBase;

        public Transform BladeTip => _bladeTip;

        public ArHandTracker Tracker => _tracker;

        /// <summary>Last unsmoothed grip pose in AR world space (valid while a hand is tracked).</summary>
        public Pose RawGripPose { get; private set; }

        public bool HasRawPose => _rawValid;

        public SwordGripMode GripMode
        {
            get => _gripMode;
            set => _gripMode = value;
        }

        public void Configure(ArHandTracker tracker, ArEncounterSceneController encounter, Transform swordRoot, Transform bladeBase, Transform bladeTip)
        {
            _tracker = tracker;
            _encounter = encounter;
            _swordRoot = swordRoot;
            _bladeBase = bladeBase;
            _bladeTip = bladeTip;
        }

        private void Awake()
        {
            _gate = new SwordHitGate(_swing);
            if (_swordRoot != null)
            {
                _swordRoot.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            ReadObservation();
            bool fresh = _rawValid && Time.time - _rawTime <= _observationTimeoutSeconds && (_tracker == null || _tracker.IsTrackingActive);
            HandPose input = default;
            if (fresh)
            {
                input.Valid = true;
                input.GripPosition = ToN(RawGripPose.position);
                input.GripRotation = ToN(RawGripPose.rotation);
            }

            SmoothedHandPose smoothed = _smoother.Update(Time.time, input);
            IsHolding = smoothed.Visible && smoothed.Holding;
            if (_swordRoot != null)
            {
                if (_swordRoot.gameObject.activeSelf != smoothed.Visible)
                {
                    _swordRoot.gameObject.SetActive(smoothed.Visible);
                }

                if (smoothed.Visible)
                {
                    _swordRoot.SetPositionAndRotation(ToU(smoothed.GripPosition), ToU(smoothed.GripRotation));
                }
            }

            CheckHit(smoothed.Visible, smoothed.Visible && !smoothed.Holding && fresh);
        }

        private void ReadObservation()
        {
            HandObservation o = _tracker != null ? _tracker.Latest : null;
            if (o == null || o.Sequence == _lastSequence || o.Sequence == 0)
            {
                return;
            }

            _lastSequence = o.Sequence;
            if (!o.HasHand)
            {
                _rawValid = false;
                return;
            }

            Matrix4x4 p = o.CameraProjection;
            var projection = new CameraProjection(p.m00, p.m11, p.m02, p.m12);
            _estimator.GripMode = _gripMode;
            HandPose pose = _estimator.Estimate(o.Viewport, o.World, projection);
            if (!pose.Valid)
            {
                _rawValid = false;
                return;
            }

            Vector3 world = o.CameraPosition + o.CameraRotation * ToU(pose.GripPosition);
            Quaternion rotation = o.CameraRotation * ToU(pose.GripRotation);
            RawGripPose = new Pose(world, rotation);
            _rawValid = true;
            _rawTime = Time.time;
        }

        private void CheckHit(bool visible, bool tracked)
        {
            if (_gate == null || _bladeBase == null || _bladeTip == null)
            {
                return;
            }

            Vector3 basePos = _bladeBase.position;
            Vector3 tipPos = _bladeTip.position;
            ArEncounterMob mob = _encounter != null ? _encounter.SpawnedMob : null;
            bool contact = false;
            if (visible && mob != null)
            {
                Transform t = mob.transform;
                Vector3 prevBase = _hasPrevious ? _previousBase : basePos;
                Vector3 prevTip = _hasPrevious ? _previousTip : tipPos;
                contact = SwordGeometry.SweptBladeHitsBox(
                    ToN(t.InverseTransformPoint(prevBase)),
                    ToN(t.InverseTransformPoint(prevTip)),
                    ToN(t.InverseTransformPoint(basePos)),
                    ToN(t.InverseTransformPoint(tipPos)),
                    _bladeRadius,
                    ToN(mob.HitVolumeCenter),
                    ToN(mob.HitVolumeHalfExtents));
            }

            if (contact)
            {
                ContactFrames++;
            }

            LastDecision = _gate.Update(Time.time, ToN(tipPos), tracked, contact);
            if (LastDecision == SwordHitDecision.Hit && mob != null)
            {
                HitCount++;
                var info = new ArSwordHitInfo
                {
                    HitNumber = HitCount,
                    Time = Time.time,
                    TipSpeed = _gate.TipSpeed,
                    TipPosition = tipPos,
                    MobName = mob.Request != null ? mob.Request.MobName : mob.name,
                };
                LastHit = info;
                mob.RegisterSwordHit();
                Debug.Log("[ArSword] hit #" + HitCount + " on " + info.MobName + " tipSpeed=" + info.TipSpeed.ToString("0.00") + " m/s (prototype, no damage)");
                if (_encounter != null && _encounter.Hud != null)
                {
                    _encounter.Hud.ShowToast(DefaultHitToast);
                }

                SwordHit?.Invoke(info);
            }

            _previousBase = basePos;
            _previousTip = tipPos;
            _hasPrevious = visible;
        }

        private static NVector3 ToN(Vector3 v) => new NVector3(v.x, v.y, v.z);

        private static NQuaternion ToN(Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);

        private static Vector3 ToU(NVector3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Quaternion ToU(NQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
