using System;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>Which visible FPP model is shown. Only one exists at a time.</summary>
    public enum FppViewVariant
    {
        /// <summary>#34 / PR #36 rig + Idle/Attack/AuraActivate clips on the #31 timeline (integration preview default).</summary>
        Art,
        /// <summary>Original #31 procedural blockout, kept as a comparison variant.</summary>
        Procedural,
    }

    /// <summary>First-person button combat over the existing world-anchored encounter.</summary>
    public sealed class FppCombatController : MonoBehaviour
    {
        private ArEncounterSceneController _encounter;
        private IFppHandsView _view;
        private FppViewVariant _variant = FppViewVariant.Art;
        private bool _hasPreview;
        private FppCombatAction _previewAction;
        private float _previewElapsed, _previewAuraRemaining;
        private bool _paused, _unfocused;
        private bool _hasCameraPose;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        public FppCombatTimeline Timeline { get; } = new FppCombatTimeline();
        public IFppHandsView View => ViewAlive ? _view : null;
        private bool ViewAlive => _view is UnityEngine.Object o && o != null;
        public FppViewVariant Variant => _variant;
        /// <summary>Raised after the visible model was posed for this frame (test-observable).</summary>
        public event Action<FppCombatAction, float> Rendered;
        public int HitCount { get; private set; }
        public event Action<ArEncounterMob, int> HitDetected;
        public bool Ready => isActiveAndEnabled && !_paused && !_unfocused && _encounter != null
            && !_encounter.IsExiting && _encounter.State == ArEncounterState.Placed
            && _encounter.LastSessionStatus == ArSessionStatus.SessionTracking
            && _encounter.SpawnedMob != null && _encounter.ArCamera != null && ViewAlive;

        public void Initialize(ArEncounterSceneController encounter)
        {
            _encounter = encounter;
        }

        public bool RequestAttack() => !_hasPreview && Timeline.TryAttack(Ready);
        public bool RequestAura() => !_hasPreview && Timeline.TryAura(Ready);

        /// <summary>Switches the visible variant; cancels any action and rebuilds the view on the next frame.</summary>
        public void SetVariant(FppViewVariant variant)
        {
            if (variant == _variant && ViewAlive) return;
            CancelActions();
            DestroyView();
            _variant = variant;
        }

        /// <summary>
        /// Editor framing preview only: hold the visible model at a fixed pose of the contract (no input, no hits).
        /// Used for portrait screenshots; the gameplay timeline is cancelled while a preview pose is held.
        /// </summary>
        public void SetPreviewPose(FppCombatAction action, float elapsed, float auraRemaining)
        {
            Timeline.Cancel();
            _hasPreview = true;
            _previewAction = action;
            _previewElapsed = elapsed;
            _previewAuraRemaining = auraRemaining;
        }

        public void ClearPreviewPose() => _hasPreview = false;

        private void LateUpdate()
        {
            if (_encounter == null) return;
            if (!ViewAlive && _encounter.SpawnedMob != null && _encounter.ArCamera != null)
                CreateView();
            bool ready = Ready;
            if (ready)
            {
                Transform camera = _encounter.ArCamera.transform;
                // Technical discontinuity guard; never sweep a stale attack across a relocalization jump.
                if (_hasCameraPose && (Vector3.Distance(camera.position, _lastCameraPosition) > 0.25f
                    || Quaternion.Angle(camera.rotation, _lastCameraRotation) > 35f))
                    Timeline.Cancel();
                _lastCameraPosition = camera.position;
                _lastCameraRotation = camera.rotation;
                _hasCameraPose = true;
            }
            else _hasCameraPose = false;

            Timeline.Tick(Time.unscaledDeltaTime, ready && !_hasPreview);
            if (ViewAlive)
            {
                _view.gameObject.SetActive(ready);
                if (ready)
                {
                    if (_hasPreview)
                    {
                        _view.Render(_previewAction, _previewElapsed, _previewAuraRemaining, _previewElapsed);
                        Rendered?.Invoke(_previewAction, _previewElapsed);
                    }
                    else
                    {
                        if (Timeline.HasHitWindow) CheckBladeSweep();
                        _view.Render(Timeline.Action, Timeline.Elapsed, Timeline.AuraRemaining, Time.unscaledTime);
                        Rendered?.Invoke(Timeline.Action, Timeline.Elapsed);
                    }
                }
            }
            _encounter.Hud?.RefreshCombatButtons(ready, Timeline);
        }

        private void CreateView()
        {
            Renderer[] renderers = _encounter.SpawnedMob.GetComponentsInChildren<Renderer>();
            Material body = null, energy = null;
            foreach (Renderer renderer in renderers)
            {
                if (renderer.name == "Abdomen") body = renderer.sharedMaterial;
                if (renderer.name == "Eye0") energy = renderer.sharedMaterial;
            }
            // Reuse shaders/material references already shipped with the procedural AR spider. No Shader.Find stripping dependency.
            if (body == null || energy == null)
            {
                Debug.LogError("[FppCombat] Procedural spider Abdomen/Eye0 materials missing; cannot build hands.");
                enabled = false;
                return;
            }
            if (_variant == FppViewVariant.Art)
            {
                var config = Resources.Load<FppArtPreviewConfig>(FppArtPreviewConfig.ResourcePath);
                FppArtArmsView art = FppArtArmsView.Create(_encounter.ArCamera.transform, config, energy);
                if (art == null)
                {
                    // No silent fallback to the procedural variant: the art preview must be visibly broken.
                    Debug.LogError("[FppCombat] FPP art preview config/prefab/clips missing or invalid; cannot build art view.");
                    enabled = false;
                    return;
                }
                _view = art;
            }
            else
            {
                var root = new GameObject("FppHandsView");
                root.transform.SetParent(_encounter.ArCamera.transform, false);
                var procedural = root.AddComponent<FppHandsView>();
                procedural.Build(body, energy, FppPoseContract.RetestV3);
                _view = procedural;
            }
            _view.Render(FppCombatAction.Idle, 0f, 0f, 0f);
            _view.gameObject.SetActive(false);
        }

        private void DestroyView()
        {
            if (!ViewAlive) { _view = null; return; }
            GameObject go = _view.gameObject;
            _view = null;
            Destroy(go);
        }

        private void CheckBladeSweep()
        {
            ArEncounterMob mob = _encounter.SpawnedMob;
            // <= 10 ms animation samples prevent the whole active interval from being skipped at ordinary low FPS.
            int steps = Mathf.Max(1, Mathf.CeilToInt((Timeline.HitWindowEnd - Timeline.HitWindowStart) / 0.01f));
            for (int i = 0; i <= steps; i++)
            {
                float time = Mathf.Lerp(Timeline.HitWindowStart, Timeline.HitWindowEnd, i / (float)steps);
                if (!BladeIntersects(mob, time)) continue;
                if (Timeline.TryRecordHit(mob.GetEntityId().GetHashCode()))
                {
                    HitCount++;
                    _encounter.Hud?.ShowToast("Trafienie!");
                    Debug.Log("[FppCombat] hit swing=" + Timeline.SwingId + " count=" + HitCount);
                    HitDetected?.Invoke(mob, Timeline.SwingId);
                }
                break;
            }
        }

        /// <summary>Same animated blade as the view, tested against a technical body box on the procedural spider.</summary>
        public bool BladeIntersects(ArEncounterMob mob, float attackTime)
        {
            if (mob == null || _encounter == null || _encounter.ArCamera == null
                || float.IsNaN(attackTime) || float.IsInfinity(attackTime)) return false;
            FppPoseContract contract = ViewAlive ? _view.Contract : FppPoseContract.RetestV3;
            Pose pose = contract.SwordPose(FppCombatAction.Attack, attackTime);
            Transform camera = _encounter.ArCamera.transform;
            Vector3 a = mob.transform.InverseTransformPoint(camera.TransformPoint(pose.position + pose.rotation * contract.HitBladeBase));
            Vector3 b = mob.transform.InverseTransformPoint(camera.TransformPoint(pose.position + pose.rotation * contract.HitBladeTip));
            // Covers the existing abdomen/cephalothorax plus a 2.5 cm prototype tolerance. Legs/label are excluded.
            return FppHitGeometry.IntersectsBox(Point(a), Point(b), new FppPoint(-0.285, 0.125, -0.605), new FppPoint(0.285, 0.595, 0.345));
        }

        private static FppPoint Point(Vector3 p) => new FppPoint(p.x, p.y, p.z);
        public void CancelActions()
        {
            Timeline.Cancel();
            _hasPreview = false;
            _hasCameraPose = false;
            if (ViewAlive) _view.gameObject.SetActive(false);
            _encounter?.Hud?.RefreshCombatButtons(false, Timeline);
        }
        private void OnApplicationPause(bool paused) { _paused = paused; if (paused) CancelActions(); }
        private void OnApplicationFocus(bool focused) { _unfocused = !focused; if (!focused) CancelActions(); }
        private void OnDisable() => CancelActions();
        private void OnDestroy()
        {
            CancelActions();
            DestroyView();
        }
    }
}
