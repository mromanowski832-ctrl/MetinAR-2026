using System;
using System.Collections.Generic;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>PROTOTYPE swing-validity values (not final combat balance).</summary>
    [Serializable]
    public sealed class SwordSwingSettings
    {
        /// <summary>Blade-tip speed that makes a contact count as a swing hit.</summary>
        public float minSwingSpeed = 1.2f;

        /// <summary>Below this tip speed for <see cref="swingEndSeconds"/> the swing is over (a new hit becomes possible).</summary>
        public float swingEndSpeed = 0.5f;

        public float swingEndSeconds = 0.12f;

        /// <summary>Minimum time between two hits.</summary>
        public float cooldownSeconds = 0.5f;

        /// <summary>A tip step larger than this between two samples is a tracking jump, not a swing.</summary>
        public float maxTipStepMeters = 0.45f;

        /// <summary>The hand must be tracked this long before a hit can count (re-acquisition guard).</summary>
        public float minTrackedSeconds = 0.15f;

        /// <summary>Tip speed is measured over this window.</summary>
        public float speedWindowSeconds = 0.08f;

        public static SwordSwingSettings CreatePrototypeDefaults() => new SwordSwingSettings();
    }

    public enum SwordHitDecision
    {
        NoContact = 0,
        Hit = 1,
        RejectedNotTracked = 2,
        RejectedTooSlow = 3,
        RejectedSameSwing = 4,
        RejectedCooldown = 5,
        RejectedTrackingJump = 6,
        RejectedJustAcquired = 7,
    }

    /// <summary>
    /// Decides whether a blade/mob contact is a valid swing hit: the hand must be steadily tracked, the tip must move
    /// fast enough, one hit per swing, a cooldown between hits, and tracking jumps never count.
    /// </summary>
    public sealed class SwordHitGate
    {
        private readonly SwordSwingSettings _settings;
        private readonly List<(float time, Vector3 tip)> _history = new List<(float, Vector3)>();
        private bool _tracked;
        private float _trackedSince;
        private float _slowSince = -1f;
        private bool _hitThisSwing;
        private float _lastHitTime = float.NegativeInfinity;

        public SwordHitGate(SwordSwingSettings settings = null)
        {
            _settings = settings ?? SwordSwingSettings.CreatePrototypeDefaults();
        }

        public SwordSwingSettings Settings => _settings;

        /// <summary>Latest measured blade-tip speed (m/s).</summary>
        public float TipSpeed { get; private set; }

        public bool SwingActive { get; private set; }

        public int HitCount { get; private set; }

        public SwordHitDecision LastDecision { get; private set; }

        public void Reset()
        {
            _history.Clear();
            _tracked = false;
            _slowSince = -1f;
            _hitThisSwing = false;
            SwingActive = false;
            TipSpeed = 0f;
        }

        public SwordHitDecision Update(float time, Vector3 tip, bool tracked, bool contact)
        {
            LastDecision = Evaluate(time, tip, tracked, contact);
            if (LastDecision == SwordHitDecision.Hit)
            {
                HitCount++;
            }

            return LastDecision;
        }

        private SwordHitDecision Evaluate(float time, Vector3 tip, bool tracked, bool contact)
        {
            if (!tracked)
            {
                Reset();
                return contact ? SwordHitDecision.RejectedNotTracked : SwordHitDecision.NoContact;
            }

            if (!_tracked)
            {
                _tracked = true;
                _trackedSince = time;
                _history.Clear();
            }

            if (_history.Count > 0)
            {
                var last = _history[_history.Count - 1];
                if (time <= last.time)
                {
                    return contact ? SwordHitDecision.RejectedSameSwing : SwordHitDecision.NoContact;
                }

                if (Vector3.Distance(last.tip, tip) > _settings.maxTipStepMeters)
                {
                    // Re-detection / tracking glitch: restart measurement from here; nothing counts this frame.
                    _history.Clear();
                    _history.Add((time, tip));
                    _trackedSince = time;
                    SwingActive = false;
                    TipSpeed = 0f;
                    return contact ? SwordHitDecision.RejectedTrackingJump : SwordHitDecision.NoContact;
                }
            }

            _history.Add((time, tip));
            while (_history.Count > 2 && time - _history[1].time >= _settings.speedWindowSeconds)
            {
                _history.RemoveAt(0);
            }

            var oldest = _history[0];
            float dt = time - oldest.time;
            TipSpeed = dt > 1e-4f ? Vector3.Distance(oldest.tip, tip) / dt : 0f;

            if (TipSpeed >= _settings.minSwingSpeed)
            {
                SwingActive = true;
                _slowSince = -1f;
            }
            else if (TipSpeed < _settings.swingEndSpeed)
            {
                if (_slowSince < 0f)
                {
                    _slowSince = time;
                }

                if (time - _slowSince >= _settings.swingEndSeconds)
                {
                    SwingActive = false;
                    _hitThisSwing = false;
                }
            }

            if (!contact)
            {
                return SwordHitDecision.NoContact;
            }

            if (time - _trackedSince < _settings.minTrackedSeconds)
            {
                return SwordHitDecision.RejectedJustAcquired;
            }

            if (_hitThisSwing)
            {
                return SwordHitDecision.RejectedSameSwing;
            }

            if (TipSpeed < _settings.minSwingSpeed)
            {
                return SwordHitDecision.RejectedTooSlow;
            }

            if (time - _lastHitTime < _settings.cooldownSeconds)
            {
                return SwordHitDecision.RejectedCooldown;
            }

            _hitThisSwing = true;
            _lastHitTime = time;
            return SwordHitDecision.Hit;
        }
    }

    /// <summary>Blade-vs-mob intersection helpers (box in the mob's local space, blade as a thick segment).</summary>
    public static class SwordGeometry
    {
        /// <summary>Slab test: does segment a→b touch the axis-aligned box (centre, half extents)?</summary>
        public static bool SegmentIntersectsBox(Vector3 a, Vector3 b, Vector3 center, Vector3 halfExtents)
        {
            Vector3 d = b - a;
            float tMin = 0f;
            float tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = Component(a, axis) - Component(center, axis);
                float dir = Component(d, axis);
                float half = Component(halfExtents, axis);
                if (MathF.Abs(dir) < 1e-9f)
                {
                    if (origin < -half || origin > half)
                    {
                        return false;
                    }

                    continue;
                }

                float t1 = (-half - origin) / dir;
                float t2 = (half - origin) / dir;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }

                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);
                if (tMin > tMax)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Blade (hilt → tip, thickness <paramref name="radius"/>) swept from the previous to the current pose, sampled in
        /// <paramref name="steps"/> intermediate segments so a fast swing cannot tunnel through the mob between frames.
        /// </summary>
        public static bool SweptBladeHitsBox(
            Vector3 previousHilt,
            Vector3 previousTip,
            Vector3 hilt,
            Vector3 tip,
            float radius,
            Vector3 center,
            Vector3 halfExtents,
            int steps = 4)
        {
            Vector3 inflated = halfExtents + new Vector3(radius, radius, radius);
            steps = Math.Max(1, steps);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                if (SegmentIntersectsBox(Vector3.Lerp(previousHilt, hilt, t), Vector3.Lerp(previousTip, tip, t), center, inflated))
                {
                    return true;
                }
            }

            return false;
        }

        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    }
}
