using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>Visible first-person hands/sword variant. Exactly one variant exists at a time (no bone is driven twice).</summary>
    public interface IFppHandsView
    {
        /// <summary>Pose contract the visible model follows; hit sampling uses the same contract.</summary>
        FppPoseContract Contract { get; }
        /// <summary>Right grip frame (sword pivot): blade along local +Y.</summary>
        Transform RightGrip { get; }
        Transform LeftHand { get; }
        GameObject gameObject { get; }
        void Render(FppCombatAction action, float elapsed, float auraRemaining, float visualTime);
    }
}
