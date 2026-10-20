using UnityEngine;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// Latest hand observation published by <see cref="ArHandTracker"/>: landmarks in AR-camera viewport coordinates,
    /// the model's metric world landmarks, and the AR camera pose at the moment the camera image was taken.
    /// </summary>
    public sealed class HandObservation
    {
        public int Sequence;

        public bool HasHand;

        /// <summary>Time.time when the observation was published.</summary>
        public float Time;

        public float Presence;

        public float Handedness;

        public float PalmScore;

        /// <summary>True when the region came from the previous frame's landmarks (palm detector skipped).</summary>
        public bool FromTracking;

        public float InferenceMilliseconds;

        /// <summary>"camera" (live AR camera + model) or "override" (test seam).</summary>
        public string Source = "camera";

        public readonly NVector2[] Viewport = new NVector2[HandLandmark.Count];

        public readonly NVector3[] World = new NVector3[HandLandmark.Count];

        /// <summary>AR camera pose (world) when the frame was captured; the hand pose is relative to it.</summary>
        public Vector3 CameraPosition;

        public Quaternion CameraRotation = Quaternion.identity;

        public Matrix4x4 CameraProjection = Matrix4x4.identity;

        public void CopyFrom(HandObservation other)
        {
            Sequence = other.Sequence;
            HasHand = other.HasHand;
            Time = other.Time;
            Presence = other.Presence;
            Handedness = other.Handedness;
            PalmScore = other.PalmScore;
            FromTracking = other.FromTracking;
            InferenceMilliseconds = other.InferenceMilliseconds;
            Source = other.Source;
            CameraPosition = other.CameraPosition;
            CameraRotation = other.CameraRotation;
            CameraProjection = other.CameraProjection;
            System.Array.Copy(other.Viewport, Viewport, HandLandmark.Count);
            System.Array.Copy(other.World, World, HandLandmark.Count);
        }
    }
}
