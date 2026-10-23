using MetinAR.ArEncounter;
using UnityEngine;

namespace MetinAR.Presentation
{
    /// <summary>
    /// Samples the existing #36 generic clips on the same clock as combat. The hit-sweep geometry stays
    /// authoritative. A visibly misaligned rig never replaces it, including after a future framing change.
    /// </summary>
    public sealed class FppModelPresenter : MonoBehaviour
    {
        public const float BladeToleranceMetres = 0.02f;
        private FppHandsView _view;
        private GameObject _model;
        private Animator _animator;
        private AnimationClip _idle, _attack, _aura;
        private Transform _bladeBase, _bladeTip;
        private Renderer[] _modelRenderers;
        private MeshRenderer[] _blockoutRenderers;
        private bool _reportedMismatch;

        public bool IsProductionVisible { get; private set; }
        public float LastBladeErrorMetres { get; private set; }

        public static FppModelPresenter Attach(FppHandsView view, GameObject prefab)
        {
            if (view == null || prefab == null) return null;
            var presenter = view.gameObject.AddComponent<FppModelPresenter>();
            if (presenter.Initialize(view, prefab)) return presenter;
            if (Application.isPlaying) Destroy(presenter); else DestroyImmediate(presenter);
            return null;
        }

        private bool Initialize(FppHandsView view, GameObject prefab)
        {
            _view = view;
            // Capture before instantiating: never accidentally hide the replacement's renderers.
            _blockoutRenderers = view.GetComponentsInChildren<MeshRenderer>(true);
            _model = Instantiate(prefab, view.transform, false);
            _model.name = "ProductionFppArms";
            _animator = _model.GetComponentInChildren<Animator>(true);
            _modelRenderers = _model.GetComponentsInChildren<Renderer>(true);
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return Reject("Missing production Animator/controller.");

            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "A_FppArms_Idle") _idle = clip;
                else if (clip.name == "A_FppArms_Attack") _attack = clip;
                else if (clip.name == "A_FppArms_AuraActivate") _aura = clip;
            }
            _bladeBase = Find(_model.transform, "Socket_FxBladeBase");
            _bladeTip = Find(_model.transform, "Socket_FxBladeTip");
            if (_idle == null || _attack == null || _aura == null || _bladeBase == null || _bladeTip == null)
                return Reject("Missing production clips or blade sockets.");
            if (Mathf.Abs(_attack.length - FppCombatTimeline.AttackSeconds) > 0.01f
                || Mathf.Abs(_aura.length - FppCombatTimeline.AuraCastSeconds) > 0.01f)
                return Reject("Production clip durations differ from the combat timeline.");

            // These are Generic transform clips, not Humanoid clips. Sampling is local to this instance,
            // leaves shared clips untouched, and prevents an independent Animator clock or root motion.
            _animator.applyRootMotion = false;
            _animator.enabled = false;
            foreach (Collider collider in _model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (SkinnedMeshRenderer renderer in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.updateWhenOffscreen = true;
            SetVisibility(false);
            return true;
        }

        private bool Reject(string reason)
        {
            Debug.LogError("[MetinAR Art] " + reason + " Keeping the existing FPP view.");
            if (_model != null)
            {
                _model.SetActive(false);
                if (Application.isPlaying) Destroy(_model); else DestroyImmediate(_model);
            }
            return false;
        }

        public void Render(FppCombatTimeline timeline, float visualTime)
        {
            bool aligned = Sample(timeline.Action, timeline.Elapsed, visualTime);
            SetVisibility(aligned);
            if (!aligned && !_reportedMismatch)
            {
                _reportedMismatch = true;
                Debug.LogError("[MetinAR Art] FPP blade mismatch " + LastBladeErrorMetres.ToString("F4")
                    + " m. Production visuals rejected; run PresentationIntegrationCheck before shipping.");
            }
        }

        /// <summary>Also used by the Unity acceptance gate against the actual FppHandsView hit contract.</summary>
        public bool Sample(FppCombatAction action, float elapsed, float visualTime)
        {
            if (_animator == null || _bladeBase == null || _bladeTip == null) return false;
            AnimationClip clip = action == FppCombatAction.Attack ? _attack
                : action == FppCombatAction.Aura ? _aura : _idle;
            float time = action == FppCombatAction.Idle ? Mathf.Repeat(visualTime, clip.length)
                : Mathf.Clamp(elapsed, 0f, clip.length);
            clip.SampleAnimation(_animator.gameObject, time);
            Pose expected = FppHandsView.SwordPose(action, elapsed);
            Vector3 expectedBase = _view.transform.TransformPoint(expected.position + expected.rotation * FppHandsView.BladeBase);
            Vector3 expectedTip = _view.transform.TransformPoint(expected.position + expected.rotation * FppHandsView.BladeTip);
            LastBladeErrorMetres = Mathf.Max(Vector3.Distance(_bladeBase.position, expectedBase),
                Vector3.Distance(_bladeTip.position, expectedTip));
            return !float.IsNaN(LastBladeErrorMetres) && !float.IsInfinity(LastBladeErrorMetres)
                && LastBladeErrorMetres <= BladeToleranceMetres;
        }

        private void SetVisibility(bool production)
        {
            IsProductionVisible = production;
            foreach (Renderer renderer in _modelRenderers) if (renderer != null) renderer.enabled = production;
            foreach (MeshRenderer renderer in _blockoutRenderers) if (renderer != null) renderer.enabled = !production;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
