using System;
using System.Collections.Generic;

namespace MetinAR.Map
{
    /// <summary>Prototype thresholds for <see cref="MovementSpeedEstimator"/> (GPS-noise values, not balance).</summary>
    public sealed class MovementSpeedSettings
    {
        /// <summary>Samples older than this (s) leave the window.</summary>
        public double WindowSeconds = 5.0;

        /// <summary>No estimate (NaN) until the window spans at least this many seconds.</summary>
        public double MinSpanSeconds = 2.0;

        /// <summary>Displacement inside the window is reduced by this × the worst accuracy in the window (jitter).</summary>
        public double NoiseFactor = 0.5;
    }

    /// <summary>
    /// METINAR-042 (#87), rule "approach on the map, stop, then AR": a conservative walking-speed estimate from
    /// GPS fixes in local metres (east/north). Speed = max(0, displacement − NoiseFactor × worst accuracy) / span
    /// over a short window, so standing still with normal jitter reads ≈ 0 while walking (~1.4 m/s) reads clearly
    /// above <see cref="EncounterEligibilitySettings.MaxEntrySpeedMetersPerSecond"/>. NaN while unknown.
    /// Pure C#, deterministic, no coordinates kept beyond the window.
    /// </summary>
    public sealed class MovementSpeedEstimator
    {
        private readonly MovementSpeedSettings _s;
        private readonly List<Sample> _samples = new List<Sample>();

        public MovementSpeedEstimator(MovementSpeedSettings settings = null)
        {
            _s = settings ?? new MovementSpeedSettings();
        }

        /// <summary>Latest estimate (m/s), NaN when unknown.</summary>
        public double SpeedMetersPerSecond { get; private set; } = double.NaN;

        /// <summary>Adds a fix; repeated or older timestamps restart / are ignored. Returns the new estimate.</summary>
        public double OnFix(double east, double north, double accuracyMeters, double timestampSeconds)
        {
            if (double.IsNaN(east) || double.IsNaN(north) || double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds)
                || double.IsNaN(accuracyMeters) || accuracyMeters < 0)
            {
                return SpeedMetersPerSecond;
            }

            if (_samples.Count > 0)
            {
                double lastT = _samples[_samples.Count - 1].T;
                if (timestampSeconds == lastT)
                {
                    return SpeedMetersPerSecond;
                }

                if (timestampSeconds < lastT)
                {
                    Reset(); // stream restarted
                }
            }

            _samples.Add(new Sample(east, north, accuracyMeters, timestampSeconds));
            while (_samples.Count > 2 && timestampSeconds - _samples[0].T > _s.WindowSeconds)
            {
                _samples.RemoveAt(0);
            }

            Sample first = _samples[0];
            double span = timestampSeconds - first.T;
            if (span < _s.MinSpanSeconds)
            {
                return SpeedMetersPerSecond = double.NaN;
            }

            double worstAcc = 0;
            foreach (Sample x in _samples)
            {
                worstAcc = Math.Max(worstAcc, x.Acc);
            }

            double dx = east - first.E;
            double dy = north - first.N;
            double disp = Math.Sqrt(dx * dx + dy * dy);
            return SpeedMetersPerSecond = Math.Max(0, disp - _s.NoiseFactor * worstAcc) / span;
        }

        public void Reset()
        {
            _samples.Clear();
            SpeedMetersPerSecond = double.NaN;
        }

        private readonly struct Sample
        {
            public Sample(double e, double n, double acc, double t)
            {
                E = e;
                N = n;
                Acc = acc;
                T = t;
            }

            public double E { get; }

            public double N { get; }

            public double Acc { get; }

            public double T { get; }
        }
    }
}
