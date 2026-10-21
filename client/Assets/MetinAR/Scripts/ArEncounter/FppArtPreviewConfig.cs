using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Runtime references to the #34 / PR #36 FPP arms prefab and clips for the integration preview
    /// (integration/fpp-art-preview). Loaded from Resources so no scene/prefab of either source PR is modified.
    /// </summary>
    [CreateAssetMenu(menuName = "MetinAR/FPP Art Preview Config")]
    public sealed class FppArtPreviewConfig : ScriptableObject
    {
        public const string ResourcePath = "FppArtPreviewConfig";
        public GameObject ArmsPrefab;
        public AnimationClip Idle;
        public AnimationClip Attack;
        public AnimationClip AuraActivate;
        /// <summary>
        /// Id of the FppPoseContract the clips were baked from (#34 fpp_contract.py). The #36 clips up to cd3d02c are
        /// "v1-52a1375"; set "v3-a31-retest" only after #34 regenerates them from the A31-retest contract.
        /// </summary>
        public string BakedContractId = "v1-52a1375";

        public bool IsComplete => ArmsPrefab != null && Idle != null && Attack != null && AuraActivate != null;
    }
}
