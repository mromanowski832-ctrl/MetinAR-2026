using System;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// #91 (METINAR-FPP-ART-001): receiver for the animation events baked into the A_FppArms_* clips
    /// (FppArtAssetBuilder writes them from art-source/FppHandsSword/clip_reference.json). Every event calls
    /// OnFppAnimEvent(id) with one of: Windup, HitStart, HitEnd, ComboWindowOpen, ComboWindowClose, Recovered,
    /// Breath, BlockRaise, BlockActive, BlockHold, BlockRelease, AuraRaise, AuraReach, AuraGrip, AuraStabilize,
    /// AuraChargeStart, AuraSpread, AuraFlash, AuraSustain, AuraReturn.
    /// Relay only: no gameplay logic here. Hit timing for gameplay stays owned by FppCombatTimeline (#31);
    /// subscribing to <see cref="Fired"/> belongs to the #92 integration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FppArmsAnimationEvents : MonoBehaviour
    {
        public event Action<string> Fired;

        public string LastEvent { get; private set; }

        public int Count { get; private set; }

        public void OnFppAnimEvent(string id)
        {
            LastEvent = id;
            Count++;
            Fired?.Invoke(id);
        }
    }
}
