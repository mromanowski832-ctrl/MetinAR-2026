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

        public bool IsComplete => ArmsPrefab != null && Idle != null && Attack != null && AuraActivate != null;
    }
}
