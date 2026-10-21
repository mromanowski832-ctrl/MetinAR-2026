using System;

namespace MetinAR.ArEncounter
{
    /// <summary>What the AR encounter adapter must do this frame for a placed mob.</summary>
    public enum ArRecoveryAction
    {
        /// <summary>Nothing to do (tracking, or still waiting for tracking).</summary>
        None,

        /// <summary>Tracking has been lost for the configured time: reset the AR session (keep the mob object and its fight).</summary>
        ResetSession,

        /// <summary>After a reset, tracking is back: anchor the mob again on new ground, then call <see cref="ArTrackingRecovery.MarkRelocated"/>.</summary>
        Relocate,
    }

    /// <summary>
    /// #31 v0 A31 P0-2, MANAGER DECISION 18:50: while a mob is placed and the AR session is not tracking, the mob is hidden
    /// and combat is paused at once ("Szukam otoczenia…"). After <see cref="ResetAfterSeconds"/> without tracking
    /// (PROTOTYPE value, <see cref="ArPlacementSettings.trackingLostResetSeconds"/>), the ARSession is reset. The mob object
    /// and its fight (HP, the once-only reward) survive the reset; once tracking is back the mob is anchored again on new
    /// ground. A reset that does not recover is repeated after another <see cref="ResetAfterSeconds"/>. No UnityEngine dependency.
    /// </summary>
    public sealed class ArTrackingRecovery
    {
        public ArTrackingRecovery(float resetAfterSeconds)
        {
            if (!(resetAfterSeconds > 0f) || float.IsInfinity(resetAfterSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(resetAfterSeconds), "must be finite and > 0");
            }

            ResetAfterSeconds = resetAfterSeconds;
        }

        public float ResetAfterSeconds { get; }

        /// <summary>Seconds without tracking in the current loss episode (0 while tracking).</summary>
        public float LostSeconds { get; private set; }

        /// <summary>Seconds without tracking when the last reset was requested.</summary>
        public float LastResetLostSeconds { get; private set; }

        /// <summary>Total seconds without tracking since the loss began (survives repeated resets).</summary>
        public float EpisodeLostSeconds { get; private set; }

        public int ResetCount { get; private set; }

        /// <summary>A reset happened and the mob still has to be anchored again.</summary>
        public bool AwaitingRelocation { get; private set; }

        /// <summary>The placed mob may be drawn and fought: tracking and no pending relocation.</summary>
        public bool MobUsable { get; private set; } = true;

        /// <summary>Advances one frame. <paramref name="placed"/>: a mob is placed; <paramref name="tracking"/>: session tracking.</summary>
        public ArRecoveryAction Tick(bool placed, bool tracking, float deltaSeconds)
        {
            if (!placed)
            {
                LostSeconds = 0f;
                EpisodeLostSeconds = 0f;
                MobUsable = true;
                return ArRecoveryAction.None;
            }

            if (tracking)
            {
                LostSeconds = 0f;
                if (AwaitingRelocation)
                {
                    MobUsable = false;
                    return ArRecoveryAction.Relocate;
                }

                EpisodeLostSeconds = 0f;
                MobUsable = true;
                return ArRecoveryAction.None;
            }

            MobUsable = false;
            if (deltaSeconds > 0f && !float.IsInfinity(deltaSeconds))
            {
                LostSeconds += deltaSeconds;
                EpisodeLostSeconds += deltaSeconds;
            }

            if (LostSeconds < ResetAfterSeconds)
            {
                return ArRecoveryAction.None;
            }

            LastResetLostSeconds = LostSeconds;
            LostSeconds = 0f;
            ResetCount++;
            AwaitingRelocation = true;
            return ArRecoveryAction.ResetSession;
        }

        /// <summary>The mob has a new anchor on new ground after a reset.</summary>
        public void MarkRelocated()
        {
            AwaitingRelocation = false;
            EpisodeLostSeconds = 0f;
            MobUsable = true;
        }
    }
}
