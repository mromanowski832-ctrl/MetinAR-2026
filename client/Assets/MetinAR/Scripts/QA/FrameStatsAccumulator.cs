using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MetinAR.QA
{
    /// <summary>
    /// METINAR-011: pure (engine-free) rolling statistics for one sampling window of millisecond values
    /// (frame time, main-thread time, inference latency). Used by <c>MetinARPerfProbe</c> and unit-tested in
    /// tests/MetinAR.QA.Tests. Percentiles use the nearest-rank method on the window's samples.
    /// </summary>
    public sealed class FrameStatsAccumulator
    {
        private readonly List<double> _samples;
        private readonly double _spikeThresholdMs;
        private double _sum;
        private double _max;
        private int _spikes;

        public FrameStatsAccumulator(double spikeThresholdMs, int initialCapacity = 512)
        {
            if (spikeThresholdMs <= 0 || double.IsNaN(spikeThresholdMs) || double.IsInfinity(spikeThresholdMs))
            {
                throw new ArgumentOutOfRangeException(nameof(spikeThresholdMs));
            }

            _spikeThresholdMs = spikeThresholdMs;
            _samples = new List<double>(Math.Max(1, initialCapacity));
        }

        public int Count => _samples.Count;

        public double SpikeThresholdMs => _spikeThresholdMs;

        /// <summary>Adds one sample; negative, NaN and infinite values are ignored.</summary>
        public void Add(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0)
            {
                return;
            }

            _samples.Add(ms);
            _sum += ms;
            if (ms > _max)
            {
                _max = ms;
            }

            if (ms > _spikeThresholdMs)
            {
                _spikes++;
            }
        }

        public void Reset()
        {
            _samples.Clear();
            _sum = 0;
            _max = 0;
            _spikes = 0;
        }

        public FrameStatsSummary Summarize()
        {
            int n = _samples.Count;
            if (n == 0)
            {
                return new FrameStatsSummary(0, 0, 0, 0, 0, 0);
            }

            var sorted = new List<double>(_samples);
            sorted.Sort();
            return new FrameStatsSummary(n, _sum / n, Percentile(sorted, 0.50), Percentile(sorted, 0.95), _max, _spikes);
        }

        /// <summary>Nearest-rank percentile of an ascending list (p in (0, 1]).</summary>
        public static double Percentile(IReadOnlyList<double> ascending, double p)
        {
            if (ascending == null || ascending.Count == 0)
            {
                return 0;
            }

            if (p <= 0)
            {
                return ascending[0];
            }

            if (p >= 1)
            {
                return ascending[ascending.Count - 1];
            }

            int rank = (int)Math.Ceiling(p * ascending.Count);
            return ascending[Math.Min(ascending.Count, Math.Max(1, rank)) - 1];
        }
    }

    public readonly struct FrameStatsSummary
    {
        public FrameStatsSummary(int count, double averageMs, double p50Ms, double p95Ms, double maxMs, int spikes)
        {
            Count = count;
            AverageMs = averageMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            MaxMs = maxMs;
            Spikes = spikes;
        }

        public int Count { get; }
        public double AverageMs { get; }
        public double P50Ms { get; }
        public double P95Ms { get; }
        public double MaxMs { get; }
        public int Spikes { get; }

        /// <summary>Average frames per second implied by the average frame time (0 when unknown).</summary>
        public double AverageFps => AverageMs > 0 ? 1000.0 / AverageMs : 0;
    }

    /// <summary>
    /// Formats the single logcat line parsed by tools/qa (Get-MetinARPerfSamples). Format is versioned (v=1):
    /// <c>[MetinARPerf] v=1 scene=&lt;name&gt; key=value ...</c>. Keys and values never contain spaces.
    /// </summary>
    public static class PerfLogLine
    {
        public const string Tag = "[MetinARPerf]";

        public static string Format(string scene, double windowSeconds, FrameStatsSummary frame, FrameStatsSummary mainThread,
            IReadOnlyList<KeyValuePair<string, double>> extra)
        {
            var sb = new StringBuilder(256);
            sb.Append(Tag).Append(" v=1 scene=").Append(Sanitize(scene));
            Append(sb, "windowS", windowSeconds);
            sb.Append(" frames=").Append(frame.Count.ToString(CultureInfo.InvariantCulture));
            Append(sb, "fpsAvg", frame.AverageFps);
            Append(sb, "frameAvgMs", frame.AverageMs);
            Append(sb, "frameP50Ms", frame.P50Ms);
            Append(sb, "frameP95Ms", frame.P95Ms);
            Append(sb, "frameMaxMs", frame.MaxMs);
            sb.Append(" frameSpikes=").Append(frame.Spikes.ToString(CultureInfo.InvariantCulture));
            if (mainThread.Count > 0)
            {
                Append(sb, "mainAvgMs", mainThread.AverageMs);
                Append(sb, "mainP95Ms", mainThread.P95Ms);
                Append(sb, "mainMaxMs", mainThread.MaxMs);
                sb.Append(" mainSpikes=").Append(mainThread.Spikes.ToString(CultureInfo.InvariantCulture));
            }

            if (extra != null)
            {
                foreach (KeyValuePair<string, double> kv in extra)
                {
                    Append(sb, Sanitize(kv.Key), kv.Value);
                }
            }

            return sb.ToString();
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '_');
            }

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string key, double value)
        {
            sb.Append(' ').Append(key).Append('=').Append(value.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }
}
