using System.Globalization;

namespace MetinAR.ArEncounter
{
    /// <summary>What to do when an AR anchor is reported removed (#66).</summary>
    public enum ArAnchorLossAction
    {
        /// <summary>Not the monster's anchor, no monster, or the scene is exiting: nothing to do.</summary>
        Ignore,

        /// <summary>Move the monster to session space (TrackablesParent) at its current world pose; no new anchor.</summary>
        RehomeToSession,

        /// <summary>Move the monster to session space and request a new free world anchor at its current pose.</summary>
        RehomeAndReanchor,
    }

    /// <summary>
    /// Engine-free policy for #66 "placed mob disappears (mobs 0 spawns 1)". Root cause: the monster is a child of
    /// an ARAnchor GameObject and AR Foundation's ARTrackableManager destroys a removed trackable's GameObject (with
    /// all children) when destroyOnRemoval is true, without any log. XR Simulation removes a plane-attached anchor as
    /// soon as its plane is merged away (subsumed). The scene adapter turns destroyOnRemoval off for the monster's
    /// anchor and asks this guard what to do when the anchor is removed, so the monster is re-homed instead of
    /// silently destroyed, and every loss is logged as <c>[ArAnchor] event=anchorRemoved</c>.
    /// </summary>
    public sealed class ArMobAnchorGuard
    {
        /// <summary>New world anchors requested after losses before the monster stays in session space.</summary>
        public const int MaxReanchorAttempts = 3;

        public string AnchorId { get; private set; }

        public string AnchorKind { get; private set; } = "none";

        /// <summary>Anchor losses handled for the current monster.</summary>
        public int Removals { get; private set; }

        public int ReanchorAttempts { get; private set; }

        /// <summary>The monster's anchor is now <paramref name="anchorId"/> of kind world / plane.</summary>
        public void OnAnchorAttached(string anchorId, string kind)
        {
            AnchorId = anchorId;
            AnchorKind = kind ?? "none";
        }

        /// <summary>A trackable anchor was removed. Returns what the adapter must do with the monster.</summary>
        public ArAnchorLossAction OnAnchorRemoved(string removedId, bool mobAlive, bool exiting)
        {
            if (exiting || !mobAlive || string.IsNullOrEmpty(AnchorId) || removedId != AnchorId)
            {
                return ArAnchorLossAction.Ignore;
            }

            Removals++;
            AnchorId = null;
            AnchorKind = "session";
            if (ReanchorAttempts >= MaxReanchorAttempts)
            {
                return ArAnchorLossAction.RehomeToSession;
            }

            ReanchorAttempts++;
            return ArAnchorLossAction.RehomeAndReanchor;
        }

        /// <summary>
        /// The log line for a handled anchor loss. <paramref name="kindAtLoss"/> is the anchor kind before the loss
        /// (world / plane); mob coordinates are the monster's world position, which must not change.
        /// </summary>
        public string FormatRemovedLog(string removedId, string kindAtLoss, ArAnchorLossAction action, double mobX, double mobY, double mobZ)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Format(c,
                "[ArAnchor] event=anchorRemoved kind={0} id={1} action={2} reanchor={3}/{4} removals={5} mob=({6:F2}, {7:F2}, {8:F2}) mobKept=true",
                kindAtLoss, ShortId(removedId), action, ReanchorAttempts, MaxReanchorAttempts, Removals, mobX, mobY, mobZ);
        }

        /// <summary>
        /// Verdict for the monster GameObject being destroyed: <c>UNEXPECTED</c> while the encounter is still placed
        /// and not exiting (the #66 symptom), otherwise <c>expected</c>.
        /// </summary>
        public static string MobDestroyedVerdict(bool exiting, bool placed)
        {
            return !exiting && placed ? "UNEXPECTED" : "expected";
        }

        public static string FormatMobDestroyedLog(bool exiting, bool placed, string kind, string parent, int spawns)
        {
            return "[ArAnchor] event=mobDestroyed verdict=" + MobDestroyedVerdict(exiting, placed)
                   + " kind=" + (kind ?? "-") + " parent=" + (string.IsNullOrEmpty(parent) ? "-" : parent)
                   + " placed=" + (placed ? "true" : "false") + " exiting=" + (exiting ? "true" : "false")
                   + " spawns=" + spawns.ToString(CultureInfo.InvariantCulture);
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "-";
            }

            return id.Length <= 8 ? id : id.Substring(id.Length - 8);
        }
    }
}
