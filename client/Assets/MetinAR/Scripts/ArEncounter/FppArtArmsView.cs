using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Clip-driven FPP variant: #34 PF_Player_FppArms under the AR camera (identity), clips
    /// A_FppArms_Idle/Attack/AuraActivate sampled on the SAME #31 FppCombatTimeline clock (manual PlayableGraph,
    /// no Animator controller, no transitions of its own). Input, timings, cancel and the one-hit rule stay in
    /// FppCombatTimeline/FppCombatController. The procedural FppHandsView is never active at the same time.
    /// </summary>
    public sealed class FppArtArmsView : MonoBehaviour, IFppHandsView
    {
        public const string SwordSocketBone = "B_Socket_Sword";
        public const string LeftGripSocketBone = "B_Socket_GripL";

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idle, _attack, _aura;
        private FppAuraFx _auraFx;
        private float _idleLength = 2f;
        private Material _auraMaterial;

        /// <summary>The #36 clips (ce22c8b) are baked from the 52a1375 contract; changes need #34 regeneration.</summary>
        public FppPoseContract Contract => FppPoseContract.BakedClipsV1;
        public Transform RightGrip { get; private set; }
        public Transform LeftHand { get; private set; }
        public Animator Animator { get; private set; }
        public bool AuraRibbonsVisible => _auraFx != null && _auraFx.RibbonsVisible;

        public static FppArtArmsView Create(Transform arCamera, FppArtPreviewConfig config, Material energy)
        {
            if (config == null || !config.IsComplete || arCamera == null || energy == null) return null;
            GameObject root = Instantiate(config.ArmsPrefab, arCamera, false);
            root.name = "FppArtArmsView";
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            var view = root.AddComponent<FppArtArmsView>();
            if (!view.Bind(config, energy))
            {
                Destroy(root);
                return null;
            }
            return view;
        }

        private bool Bind(FppArtPreviewConfig config, Material energy)
        {
            RightGrip = FindDeep(transform, SwordSocketBone);
            LeftHand = FindDeep(transform, LeftGripSocketBone);
            Animator = GetComponentInChildren<Animator>(true);
            if (RightGrip == null || LeftHand == null || Animator == null)
            {
                Debug.LogError("[FppArt] PF_Player_FppArms is missing sockets or Animator.");
                return false;
            }
            // The clip layer is driven only by the #31 timeline below; the asset's controller would run its own clock.
            Animator.runtimeAnimatorController = null;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _graph = PlayableGraph.Create("FppArtArms");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _idle = Clip(config.Idle, 0);
            _attack = Clip(config.Attack, 1);
            _aura = Clip(config.AuraActivate, 2);
            _idleLength = Mathf.Max(0.01f, config.Idle.length);
            var output = AnimationPlayableOutput.Create(_graph, "FppArtArms", Animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _auraMaterial = new Material(energy) { name = "FppArtAura" };
            _auraFx = new FppAuraFx(RightGrip, _auraMaterial);
            Render(FppCombatAction.Idle, 0f, 0f, 0f);
            return true;
        }

        private AnimationClipPlayable Clip(AnimationClip clip, int input)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.Pause();
            _graph.Connect(playable, 0, _mixer, input);
            return playable;
        }

        public void Render(FppCombatAction action, float elapsed, float auraRemaining, float visualTime)
        {
            if (!_graph.IsValid()) return;
            _mixer.SetInputWeight(0, action == FppCombatAction.Idle ? 1f : 0f);
            _mixer.SetInputWeight(1, action == FppCombatAction.Attack ? 1f : 0f);
            _mixer.SetInputWeight(2, action == FppCombatAction.Aura ? 1f : 0f);
            _idle.SetTime(Mathf.Repeat(Mathf.Max(0f, visualTime), _idleLength));
            _attack.SetTime(action == FppCombatAction.Attack ? elapsed : 0f);
            _aura.SetTime(action == FppCombatAction.Aura ? elapsed : 0f);
            _graph.Evaluate(0f);
            _auraFx.Render(action, elapsed, auraRemaining, visualTime);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_auraMaterial != null) Destroy(_auraMaterial);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform found = FindDeep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
