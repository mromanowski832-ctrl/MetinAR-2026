using System;
using System.Collections.Generic;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>Pinhole projection terms taken from a Unity camera's projection matrix (OpenGL convention).</summary>
    public readonly struct CameraProjection
    {
        public readonly float P00;
        public readonly float P11;
        public readonly float P02;
        public readonly float P12;

        public CameraProjection(float p00, float p11, float p02, float p12)
        {
            P00 = p00;
            P11 = p11;
            P02 = p02;
            P12 = p12;
        }

        public static CameraProjection FromFieldOfView(float verticalFovDegrees, float aspect)
        {
            float f = 1f / MathF.Tan(0.5f * verticalFovDegrees * MathF.PI / 180f);
            return new CameraProjection(f / aspect, f, 0f, 0f);
        }

        /// <summary>Viewport (0..1, y up) → camera-space direction at depth 1 (x right, y up, z forward).</summary>
        public Vector2 ViewportToUnitDepth(Vector2 viewport)
        {
            return new Vector2((2f * viewport.X - 1f + P02) / P00, (2f * viewport.Y - 1f + P12) / P11);
        }

        /// <summary>Camera-space point (z forward, z &gt; 0) → viewport.</summary>
        public Vector2 CameraToViewport(Vector3 p)
        {
            float x = p.X / p.Z;
            float y = p.Y / p.Z;
            return new Vector2(0.5f * (x * P00 - P02 + 1f), 0.5f * (y * P11 - P12 + 1f));
        }
    }

    /// <summary>How the virtual sword sits in the tracked hand (PROTOTYPE choice, needs owner confirmation).</summary>
    public enum SwordGripMode
    {
        /// <summary>Closed fist: blade leaves the hand on the thumb/index side along the knuckle line (pinky → index).</summary>
        Fist = 0,

        /// <summary>Open hand: blade continues the wrist → middle-finger direction.</summary>
        Pointing = 1,
    }

    /// <summary>Result of <see cref="HandPoseEstimator"/> in camera space (metres; x right, y up, z forward).</summary>
    public struct HandPose
    {
        public bool Valid;

        /// <summary>Sword grip centre (palm centre).</summary>
        public Vector3 GripPosition;

        /// <summary>Local +Z = blade direction, local +Y = sword "up" (edge plane).</summary>
        public Quaternion GripRotation;

        public Vector3 BladeDirection;

        public Vector3 Up;

        /// <summary>Estimated hand distance from the camera.</summary>
        public float Depth;

        /// <summary>True when the depth estimate hit the configured clamp range.</summary>
        public bool DepthClamped;
    }

    /// <summary>
    /// Lifts 2-D landmarks (viewport) into camera space with a weak-perspective fit: the model's metric world landmarks
    /// give the true hand size, the projected landmark spread gives the distance. Then derives the sword grip pose.
    /// Pure C#; no camera intrinsics needed beyond the Unity camera's projection.
    /// </summary>
    public sealed class HandPoseEstimator
    {
        public float MinDepth = 0.12f;
        public float MaxDepth = 2.0f;

        /// <summary>Fallback when no metric landmarks are available: index-MCP ↔ pinky-MCP distance of an adult hand.</summary>
        public float FallbackKnuckleSpanMeters = 0.075f;

        public SwordGripMode GripMode = SwordGripMode.Fist;

        private readonly Vector2[] _unit = new Vector2[HandLandmark.Count];

        /// <summary>Camera-space landmarks of the last successful estimate.</summary>
        public readonly Vector3[] CameraPoints = new Vector3[HandLandmark.Count];

        public HandPose Estimate(IReadOnlyList<Vector2> viewport, IReadOnlyList<Vector3> worldPoints, CameraProjection projection)
        {
            var pose = new HandPose { Valid = false };
            if (viewport == null || viewport.Count < HandLandmark.Count)
            {
                return pose;
            }

            Vector2 meanUnit = Vector2.Zero;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                Vector2 v = viewport[i];
                if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsInfinity(v.X) || float.IsInfinity(v.Y))
                {
                    return pose;
                }

                _unit[i] = projection.ViewportToUnitDepth(v);
                meanUnit += _unit[i];
            }

            meanUnit /= HandLandmark.Count;

            bool hasWorld = worldPoints != null && worldPoints.Count >= HandLandmark.Count;
            Vector3 meanWorld = Vector3.Zero;
            if (hasWorld)
            {
                for (int i = 0; i < HandLandmark.Count; i++)
                {
                    meanWorld += worldPoints[i];
                }

                meanWorld /= HandLandmark.Count;
            }

            float projectedSpread = 0f;
            float metricSpread = 0f;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                projectedSpread += (_unit[i] - meanUnit).Length();
                if (hasWorld)
                {
                    Vector3 d = worldPoints[i] - meanWorld;
                    metricSpread += MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                }
            }

            float depth;
            if (hasWorld && metricSpread > 1e-4f && projectedSpread > 1e-6f)
            {
                // Weak-perspective start, then a few fixed-point steps that account for each landmark's own depth
                // (perspective): find the centre depth whose reconstructed x/y spread equals the metric spread.
                depth = metricSpread / projectedSpread;
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    float spread = ReconstructedSpread(depth, worldPoints, meanWorld.Z);
                    if (spread < 1e-6f)
                    {
                        break;
                    }

                    depth *= metricSpread / spread;
                }
            }
            else
            {
                float span = (_unit[HandLandmark.IndexMcp] - _unit[HandLandmark.PinkyMcp]).Length();
                if (span < 1e-6f)
                {
                    return pose;
                }

                depth = FallbackKnuckleSpanMeters / span;
            }

            if (float.IsNaN(depth) || float.IsInfinity(depth))
            {
                return pose;
            }

            float clamped = Math.Clamp(depth, MinDepth, MaxDepth);
            pose.DepthClamped = MathF.Abs(clamped - depth) > 1e-6f;
            depth = clamped;

            for (int i = 0; i < HandLandmark.Count; i++)
            {
                float dz = hasWorld ? worldPoints[i].Z - meanWorld.Z : 0f;
                float d = MathF.Max(MinDepth * 0.5f, depth + dz);
                CameraPoints[i] = new Vector3(_unit[i].X * d, _unit[i].Y * d, d);
            }

            Vector3 q0 = CameraPoints[HandLandmark.Wrist];
            Vector3 q5 = CameraPoints[HandLandmark.IndexMcp];
            Vector3 q9 = CameraPoints[HandLandmark.MiddleMcp];
            Vector3 q13 = CameraPoints[HandLandmark.RingMcp];
            Vector3 q17 = CameraPoints[HandLandmark.PinkyMcp];
            Vector3 knuckles = 0.25f * (q5 + q9 + q13 + q17);
            Vector3 grip = 0.4f * q0 + 0.6f * knuckles;

            Vector3 across = q5 - q17;
            Vector3 along = q9 - q0;
            if (across.LengthSquared() < 1e-10f || along.LengthSquared() < 1e-10f)
            {
                return pose;
            }

            across = Vector3.Normalize(across);
            along -= Vector3.Dot(along, across) * across;
            if (along.LengthSquared() < 1e-10f)
            {
                return pose;
            }

            along = Vector3.Normalize(along);
            Vector3 normal = Vector3.Normalize(Vector3.Cross(across, along));

            Vector3 forward;
            Vector3 up;
            if (GripMode == SwordGripMode.Pointing)
            {
                forward = along;
                up = normal;
            }
            else
            {
                forward = across;
                up = along;
            }

            pose.Valid = true;
            pose.GripPosition = grip;
            pose.BladeDirection = forward;
            pose.Up = up;
            pose.GripRotation = LookRotation(forward, up);
            pose.Depth = depth;
            return pose;
        }

        private float ReconstructedSpread(float centreDepth, IReadOnlyList<Vector3> worldPoints, float meanWorldZ)
        {
            Vector2 mean = Vector2.Zero;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                mean += _unit[i] * (centreDepth + worldPoints[i].Z - meanWorldZ);
            }

            mean /= HandLandmark.Count;
            float spread = 0f;
            for (int i = 0; i < HandLandmark.Count; i++)
            {
                spread += (_unit[i] * (centreDepth + worldPoints[i].Z - meanWorldZ) - mean).Length();
            }

            return spread;
        }

        /// <summary>Unity-style LookRotation: rotates +Z onto <paramref name="forward"/> and +Y toward <paramref name="up"/>.</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 f = Vector3.Normalize(forward);
            Vector3 r = Vector3.Cross(up, f);
            if (r.LengthSquared() < 1e-10f)
            {
                r = Vector3.Cross(MathF.Abs(f.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, f);
            }

            r = Vector3.Normalize(r);
            Vector3 u = Vector3.Cross(f, r);
            var m = new Matrix4x4(
                r.X, r.Y, r.Z, 0f,
                u.X, u.Y, u.Z, 0f,
                f.X, f.Y, f.Z, 0f,
                0f, 0f, 0f, 1f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }
    }
}
