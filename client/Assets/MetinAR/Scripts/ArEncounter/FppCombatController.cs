using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>First-person button combat over the existing world-anchored encounter.</summary>
    public sealed class FppCombatController : MonoBehaviour
    {
        private ArEncounterSceneController _encounter;
        private FppHandsView _view;
        private bool _paused, _unfocused;
        private bool _hasCameraPose;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        public FppCombatTimeline Timeline { get; } = new FppCombatTimeline();
        public FppHandsView View => _view;
        public int HitCount { get; private set; }
        public event Action<ArEncounterMob, int> HitDetected;
        public bool Ready => isActiveAndEnabled && !_paused && !_unfocused && _encounter != null
            && !_encounter.IsExiting && _encounter.State == ArEncounterState.Placed
            && _encounter.LastSessionStatus == ArSessionStatus.SessionTracking && _encounter.MobUsable
            && _encounter.SpawnedMob != null && _encounter.ArCamera != null && _view != null;

        public void Initialize(ArEncounterSceneController encounter)
        {
            _encounter = encounter;
        }

        public bool RequestAttack() => Timeline.TryAttack(Ready);
        public bool RequestAura() => Timeline.TryAura(Ready);

        private void LateUpdate()
        {
            if (_encounter == null) return;
            if (_view == null && _encounter.SpawnedMob != null && _encounter.ArCamera != null)
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

            Timeline.Tick(Time.unscaledDeltaTime, ready);
            if (_view != null)
            {
                _view.gameObject.SetActive(ready);
                if (ready)
                {
                    if (Timeline.HasHitWindow) CheckBladeSweep();
                    _view.Render(Timeline, Time.unscaledTime);
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
            var root = new GameObject("FppHandsView");
            root.transform.SetParent(_encounter.ArCamera.transform, false);
            _view = root.AddComponent<FppHandsView>();
            _view.Build(body, energy);
            _view.Render(Timeline, 0f);
            root.SetActive(false);
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
            Pose pose = FppHandsView.SwordPose(FppCombatAction.Attack, attackTime);
            Transform camera = _encounter.ArCamera.transform;
            Vector3 a = mob.transform.InverseTransformPoint(camera.TransformPoint(pose.position + pose.rotation * FppHandsView.BladeBase));
            Vector3 b = mob.transform.InverseTransformPoint(camera.TransformPoint(pose.position + pose.rotation * FppHandsView.BladeTip));
            // Body footprint (abdomen/cephalothorax + 2.5 cm) extruded vertically: the floor spider is reachable from a
            // handheld phone height (#31 v0 A31 P0-1). Legs/label are excluded; horizontal reach is unchanged.
            return FppHitGeometry.IntersectsReachColumn(Point(a), Point(b));
        }

        private static float _bladeReach = -1f;

        /// <summary>
        /// #31 decision 18:58 correction: the blade's horizontal reach from the camera, independent of aim — derived from the
        /// SAME animated sword pose and blade segment as the hit sweep (<see cref="FppHandsView.SwordPose"/>,
        /// <see cref="FppHandsView.BladeBase"/>/<see cref="FppHandsView.BladeTip"/>, ATAK hit window), via
        /// <see cref="ArApproachPrompt.BladeReach"/>. No new constant. Used for the „Podejdź” prompt only.
        /// </summary>
        public static float BladeReachMeters
        {
            get
            {
                if (_bladeReach >= 0f) return _bladeReach;
                const int samples = 48;
                var points = new List<FppPoint>(2 * (samples + 1));
                for (int i = 0; i <= samples; i++)
                {
                    float t = Mathf.Lerp(FppCombatTimeline.HitStartSeconds, FppCombatTimeline.HitEndSeconds, i / (float)samples);
                    Pose pose = FppHandsView.SwordPose(FppCombatAction.Attack, t);
                    points.Add(Point(pose.position + pose.rotation * FppHandsView.BladeBase));
                    points.Add(Point(pose.position + pose.rotation * FppHandsView.BladeTip));
                }

                _bladeReach = (float)ArApproachPrompt.BladeReach(points);
                return _bladeReach;
            }
        }

        /// <summary>
        /// Horizontal world distance from the AR camera to the mob's hit footprint (<see cref="FppHitGeometry.BodyMin"/> ..
        /// <see cref="FppHitGeometry.ReachColumnMax"/> in x/z, mob scale applied). Aim-independent. NaN without camera/mob.
        /// </summary>
        public float HorizontalDistanceToHitFootprint(ArEncounterMob mob)
        {
            if (mob == null || _encounter == null || _encounter.ArCamera == null) return float.NaN;
            Vector3 cam = _encounter.ArCamera.transform.position;
            Vector3 local = mob.transform.InverseTransformPoint(cam);
            FppPoint max = FppHitGeometry.ReachColumnMax;
            float x = Mathf.Clamp(local.x, (float)FppHitGeometry.BodyMin.X, (float)max.X);
            float z = Mathf.Clamp(local.z, (float)FppHitGeometry.BodyMin.Z, (float)max.Z);
            Vector3 nearest = mob.transform.TransformPoint(new Vector3(x, local.y, z));
            Vector2 d = new Vector2(nearest.x - cam.x, nearest.z - cam.z);
            return d.magnitude;
        }

        /// <summary>
        /// Hit test only (not the prompt): true when an ATAK swung from the current camera pose — including where it aims —
        /// would hit the mob, using the same blade sweep over the hit window as <see cref="CheckBladeSweep"/>.
        /// </summary>
        public bool SwingWouldHit(ArEncounterMob mob)
        {
            const int samples = 48;
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(FppCombatTimeline.HitStartSeconds, FppCombatTimeline.HitEndSeconds, i / (float)samples);
                if (BladeIntersects(mob, t)) return true;
            }

            return false;
        }

        private static FppPoint Point(Vector3 p) => new FppPoint(p.x, p.y, p.z);
        public void CancelActions()
        {
            Timeline.Cancel();
            _hasCameraPose = false;
            if (_view != null) _view.gameObject.SetActive(false);
            _encounter?.Hud?.RefreshCombatButtons(false, Timeline);
        }
        private void OnApplicationPause(bool paused) { _paused = paused; if (paused) CancelActions(); }
        private void OnApplicationFocus(bool focused) { _unfocused = !focused; if (!focused) CancelActions(); }
        private void OnDisable() => CancelActions();
        private void OnDestroy()
        {
            CancelActions();
            if (_view != null) Destroy(_view.gameObject);
        }
    }
}
