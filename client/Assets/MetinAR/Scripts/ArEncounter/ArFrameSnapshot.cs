using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// One frame of AR input for <see cref="ArEncounterSceneController"/>: session status, detected planes and
    /// the current placement target. Captured from AR Foundation each frame; tests may supply it through
    /// <see cref="ArEncounterSceneController.FrameSourceOverride"/>.
    /// </summary>
    public sealed class ArFrameSnapshot
    {
        public ArSessionStatus SessionStatus;

        public readonly List<ArPlaneObservation> Planes = new List<ArPlaneObservation>();

        /// <summary>A placement target on a suitable plane exists.</summary>
        public bool HasTarget;

        /// <summary>World-space pose for the monster (on the plane, facing the camera).</summary>
        public Pose TargetPose;

        /// <summary>Plane id of the target (matches an entry of <see cref="Planes"/>).</summary>
        public string TargetPlaneId;

        /// <summary>The AR Foundation plane of the target (null for synthetic test planes).</summary>
        public ARPlane TargetPlane;

        /// <summary>True when the target came from the screen-centre reticle raycast (otherwise: plane centre fallback).</summary>
        public bool TargetFromReticle;

        /// <summary>Suitable ground was hit, but only closer than the minimum placement distance.</summary>
        public bool TargetTooClose;

        /// <summary>Suitable ground was hit, but only more than the allowed height below the camera (#31 P1).</summary>
        public bool TargetTooLow;

        public void Clear()
        {
            SessionStatus = ArSessionStatus.None;
            Planes.Clear();
            HasTarget = false;
            TargetPose = Pose.identity;
            TargetPlaneId = null;
            TargetPlane = null;
            TargetFromReticle = false;
            TargetTooClose = false;
            TargetTooLow = false;
        }
    }
}
