using System;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>One Euro filter (Casiez et al. 2012) for a 3-D point: little jitter at rest, little lag when moving fast.</summary>
    public sealed class OneEuroFilter3
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        private bool _initialised;
        private float _lastTime;
        private Vector3 _value;
        private Vector3 _derivative;

        public OneEuroFilter3(float minCutoff, float beta, float derivativeCutoff)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public bool IsInitialised => _initialised;

        public Vector3 Value => _value;

        public static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * MathF.PI * MathF.Max(cutoff, 1e-4f));
            return 1f / (1f + tau / dt);
        }

        public void Reset()
        {
            _initialised = false;
            _derivative = Vector3.Zero;
        }

        public Vector3 Filter(Vector3 x, float time)
        {
            if (!_initialised)
            {
                _initialised = true;
                _lastTime = time;
                _value = x;
                _derivative = Vector3.Zero;
                return x;
            }

            float dt = time - _lastTime;
            if (dt <= 1e-5f)
            {
                return _value;
            }

            _lastTime = time;
            Vector3 dx = (x - _value) / dt;
            _derivative = Vector3.Lerp(_derivative, dx, Alpha(DerivativeCutoff, dt));
            float cutoff = MinCutoff + Beta * _derivative.Length();
            _value = Vector3.Lerp(_value, x, Alpha(cutoff, dt));
            return _value;
        }
    }

    /// <summary>Smoothed, possibly held hand pose for rendering the sword.</summary>
    public struct SmoothedHandPose
    {
        public bool Visible;

        /// <summary>True while the last pose is held after a short tracking drop-out.</summary>
        public bool Holding;

        public Vector3 GripPosition;

        public Quaternion GripRotation;
    }

    /// <summary>
    /// Stabilises the raw per-inference hand pose: One Euro filter on position, speed-adaptive slerp on rotation,
    /// brief hold (default 0.25 s) across detection drop-outs, then hide; snaps instead of smearing on large jumps.
    /// </summary>
    public sealed class HandPoseSmoother
    {
        public float HoldSeconds = 0.25f;
        public float SnapDistance = 0.35f;
        public float RotationMinCutoff = 2.0f;
        public float RotationBeta = 0.6f;

        private readonly OneEuroFilter3 _position;
        private bool _hasPose;
        private float _lastValidTime;
        private float _lastRotationTime;
        private Quaternion _rotation = Quaternion.Identity;
        private SmoothedHandPose _current;

        public HandPoseSmoother(float positionMinCutoff = 1.5f, float positionBeta = 4f, float derivativeCutoff = 1f)
        {
            _position = new OneEuroFilter3(positionMinCutoff, positionBeta, derivativeCutoff);
        }

        public SmoothedHandPose Current => _current;

        public void Reset()
        {
            _position.Reset();
            _hasPose = false;
            _current = default;
        }

        public SmoothedHandPose Update(float time, HandPose raw)
        {
            if (raw.Valid)
            {
                if (_hasPose && Vector3.Distance(raw.GripPosition, _current.GripPosition) > SnapDistance)
                {
                    _position.Reset();
                    _hasPose = false;
                }

                Vector3 position = _position.Filter(raw.GripPosition, time);
                if (!_hasPose)
                {
                    _rotation = raw.GripRotation;
                }
                else
                {
                    float dt = MathF.Max(time - _lastRotationTime, 1e-4f);
                    float dot = MathF.Min(MathF.Abs(Quaternion.Dot(_rotation, raw.GripRotation)), 1f);
                    float angularSpeed = 2f * MathF.Acos(dot) / dt;
                    float alpha = OneEuroFilter3.Alpha(RotationMinCutoff + RotationBeta * angularSpeed, dt);
                    _rotation = Quaternion.Normalize(Quaternion.Slerp(_rotation, raw.GripRotation, alpha));
                }

                _lastRotationTime = time;
                _hasPose = true;
                _lastValidTime = time;
                _current = new SmoothedHandPose { Visible = true, Holding = false, GripPosition = position, GripRotation = _rotation };
                return _current;
            }

            if (_hasPose && time - _lastValidTime <= HoldSeconds)
            {
                _current.Visible = true;
                _current.Holding = true;
                return _current;
            }

            Reset();
            return _current;
        }
    }
}
