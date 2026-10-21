using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Camera-local pose contract of the FPP right grip (sword) and left hand (#31 / #34, see docs/METINAR-FPP-001.md).
    /// The hit window (0.18-0.42 s) only blends Windup -> End, which are shared by every contract, so a framing change of
    /// the rest pose never changes hit geometry, reach, timings or balance. Technical prototype values.
    /// </summary>
    public sealed class FppPoseContract
    {
        public static readonly Vector3 WindupPosition = new Vector3(0.25f, -0.08f, 0.48f);
        public static readonly Quaternion WindupRotation = Quaternion.Euler(0f, -10f, -65f);
        public static readonly Vector3 EndPosition = new Vector3(-0.15f, -0.22f, 0.50f);
        public static readonly Quaternion EndRotation = Quaternion.Euler(70f, 30f, 60f);
        public static readonly Vector3 AuraPosition = new Vector3(0.02f, -0.18f, 0.52f);
        public static readonly Quaternion AuraRotation = Quaternion.Euler(12f, 0f, 0f);
        public static readonly Vector3 LeftGripOffset = new Vector3(0f, -0.12f, 0f);
        public static readonly Vector3 BladeBase = new Vector3(0f, 0.13f, 0f);
        public static readonly Vector3 BladeTip = new Vector3(0f, 0.94f, 0f);
        public static readonly Vector3 GuardCentre = new Vector3(0f, 0.116f, 0f);

        /// <summary>Contract of PR #32 52a1375; the #36 clips (ce22c8b) are baked from these values.</summary>
        public static readonly FppPoseContract BakedClipsV1 = new FppPoseContract("v1-52a1375",
            new Vector3(0.18f, -0.19f, 0.46f), Quaternion.Euler(15f, 0f, -14f),
            new Vector3(-0.19f, -0.25f, 0.43f), Quaternion.Euler(0f, 0f, 12f));

        /// <summary>
        /// Reversible framing candidate (#31 controller decision 2026-10-21): right grip from the #34 proposal,
        /// left hand moved inwards so both hands and the hilt/guard are inside an estimated portrait frame.
        /// Physical A31 FOV: UNVERIFIED.
        /// </summary>
        public static readonly FppPoseContract FramingV2Candidate = new FppPoseContract("v2-framing-candidate",
            new Vector3(0.09f, -0.17f, 0.42f), Quaternion.Euler(15f, 0f, -14f),
            new Vector3(-0.08f, -0.23f, 0.42f), Quaternion.Euler(0f, 0f, 12f));

        public readonly string Id;
        public readonly Vector3 RestPosition;
        public readonly Quaternion RestRotation;
        public readonly Vector3 LeftRestPosition;
        public readonly Quaternion LeftRestRotation;

        public FppPoseContract(string id, Vector3 rest, Quaternion restRotation, Vector3 leftRest, Quaternion leftRestRotation)
        {
            Id = id;
            RestPosition = rest;
            RestRotation = restRotation;
            LeftRestPosition = leftRest;
            LeftRestRotation = leftRestRotation;
        }

        public Pose SwordPose(FppCombatAction action, float time)
        {
            if (action == FppCombatAction.Attack)
            {
                if (time < FppCombatTimeline.HitStartSeconds)
                    return Blend(RestPosition, RestRotation, WindupPosition, WindupRotation, Ease(time / FppCombatTimeline.HitStartSeconds));
                if (time <= FppCombatTimeline.HitEndSeconds)
                    return Blend(WindupPosition, WindupRotation, EndPosition, EndRotation,
                        Ease((time - FppCombatTimeline.HitStartSeconds) / (FppCombatTimeline.HitEndSeconds - FppCombatTimeline.HitStartSeconds)));
                return Blend(EndPosition, EndRotation, RestPosition, RestRotation,
                    Ease((time - FppCombatTimeline.HitEndSeconds) / (FppCombatTimeline.AttackSeconds - FppCombatTimeline.HitEndSeconds)));
            }
            if (action == FppCombatAction.Aura)
            {
                float blend = time < 0.9f ? Ease(time / 0.25f) : 1f - Ease((time - 0.9f) / 0.25f);
                return Blend(RestPosition, RestRotation, AuraPosition, AuraRotation, blend);
            }
            return new Pose(RestPosition, RestRotation);
        }

        public static float LeftGripBlend(FppCombatAction action, float time)
        {
            if (action != FppCombatAction.Aura) return 0f;
            return time < 0.9f ? Ease(time / 0.35f) : 1f - Ease((time - 0.9f) / 0.25f);
        }

        public Pose LeftHandPose(FppCombatAction action, float time)
        {
            Pose sword = SwordPose(action, time);
            float grip = LeftGripBlend(action, time);
            return new Pose(Vector3.Lerp(LeftRestPosition, sword.position + sword.rotation * LeftGripOffset, grip),
                Quaternion.Slerp(LeftRestRotation, sword.rotation, grip));
        }

        private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        private static Pose Blend(Vector3 a, Quaternion ar, Vector3 b, Quaternion br, float t)
            => new Pose(Vector3.Lerp(a, b, t), Quaternion.Slerp(ar, br, t));
    }
}
