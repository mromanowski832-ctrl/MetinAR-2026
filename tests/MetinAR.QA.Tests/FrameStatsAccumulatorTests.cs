using System.Collections.Generic;
using MetinAR.QA;
using Xunit;

namespace MetinAR.QA.Tests
{
    public class FrameStatsAccumulatorTests
    {
        [Fact]
        public void EmptyWindowSummarizesToZero()
        {
            var acc = new FrameStatsAccumulator(50);
            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(0, s.Count);
            Assert.Equal(0, s.AverageMs);
            Assert.Equal(0, s.P95Ms);
            Assert.Equal(0, s.MaxMs);
            Assert.Equal(0, s.AverageFps);
        }

        [Fact]
        public void AverageMaxAndFpsAreComputed()
        {
            var acc = new FrameStatsAccumulator(50);
            acc.Add(10);
            acc.Add(20);
            acc.Add(30);
            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(3, s.Count);
            Assert.Equal(20, s.AverageMs, 6);
            Assert.Equal(30, s.MaxMs);
            Assert.Equal(50, s.AverageFps, 6);
        }

        [Fact]
        public void SpikesCountOnlyValuesAboveThreshold()
        {
            var acc = new FrameStatsAccumulator(50);
            acc.Add(49.9);
            acc.Add(50);
            acc.Add(50.1);
            acc.Add(120);
            Assert.Equal(2, acc.Summarize().Spikes);
        }

        [Fact]
        public void InvalidSamplesAreIgnored()
        {
            var acc = new FrameStatsAccumulator(50);
            acc.Add(double.NaN);
            acc.Add(double.PositiveInfinity);
            acc.Add(-1);
            acc.Add(16);
            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(1, s.Count);
            Assert.Equal(16, s.AverageMs);
        }

        [Fact]
        public void ResetClearsWindow()
        {
            var acc = new FrameStatsAccumulator(50);
            acc.Add(100);
            acc.Reset();
            acc.Add(10);
            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(1, s.Count);
            Assert.Equal(10, s.MaxMs);
            Assert.Equal(0, s.Spikes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(double.NaN)]
        public void RejectsNonPositiveThreshold(double threshold)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FrameStatsAccumulator(threshold));
        }

        [Fact]
        public void NearestRankPercentileOnHundredSamples()
        {
            var acc = new FrameStatsAccumulator(1000);
            for (int i = 100; i >= 1; i--)
            {
                acc.Add(i); // insertion order must not matter
            }

            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(50, s.P50Ms);
            Assert.Equal(95, s.P95Ms);
            Assert.Equal(100, s.MaxMs);
        }

        [Fact]
        public void PercentileEdgeCases()
        {
            var one = new List<double> { 7 };
            Assert.Equal(7, FrameStatsAccumulator.Percentile(one, 0.95));
            var many = new List<double> { 1, 2, 3, 4 };
            Assert.Equal(1, FrameStatsAccumulator.Percentile(many, 0));
            Assert.Equal(4, FrameStatsAccumulator.Percentile(many, 1));
            Assert.Equal(2, FrameStatsAccumulator.Percentile(many, 0.5));
            Assert.Equal(0, FrameStatsAccumulator.Percentile(new List<double>(), 0.5));
            Assert.Equal(0, FrameStatsAccumulator.Percentile(null, 0.5));
        }

        [Fact]
        public void SingleSlowFrameDominatesP95InShortWindow()
        {
            var acc = new FrameStatsAccumulator(50);
            for (int i = 0; i < 19; i++)
            {
                acc.Add(16.6);
            }

            acc.Add(200);
            FrameStatsSummary s = acc.Summarize();
            Assert.Equal(16.6, s.P95Ms, 6); // rank ceil(0.95*20)=19 -> still a normal frame
            Assert.Equal(200, s.MaxMs);
            Assert.Equal(1, s.Spikes);
        }
    }

    public class PerfLogLineTests
    {
        [Fact]
        public void FormatsVersionedSpaceFreeKeyValues()
        {
            var frame = new FrameStatsSummary(300, 16.666, 16.5, 20.04, 55.5, 1);
            var main = new FrameStatsSummary(300, 9.1, 9, 12.345, 40, 0);
            var extra = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("totalUsedMB", 210.456),
                new KeyValuePair<string, double>("lat.hand inference.p95Ms", 31.2),
            };

            string line = PerfLogLine.Format("WorldMap Prototype", 5.0123, frame, main, extra);

            Assert.Equal(
                "[MetinARPerf] v=1 scene=WorldMap_Prototype windowS=5.01 frames=300 fpsAvg=60 frameAvgMs=16.67 frameP50Ms=16.5"
                + " frameP95Ms=20.04 frameMaxMs=55.5 frameSpikes=1 mainAvgMs=9.1 mainP95Ms=12.35 mainMaxMs=40 mainSpikes=0"
                + " totalUsedMB=210.46 lat.hand_inference.p95Ms=31.2",
                line);
        }

        [Fact]
        public void OmitsMainThreadWhenUnavailable()
        {
            var frame = new FrameStatsSummary(10, 33.3333, 33, 34, 35, 0);
            string line = PerfLogLine.Format("AR", 5, frame, new FrameStatsSummary(0, 0, 0, 0, 0, 0), null);
            Assert.DoesNotContain("mainAvgMs", line);
            Assert.StartsWith("[MetinARPerf] v=1 scene=AR ", line);
            Assert.Contains("fpsAvg=30", line);
        }

        [Fact]
        public void FormattingIsCultureInvariant()
        {
            System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
                string line = PerfLogLine.Format("S", 5, new FrameStatsSummary(1, 12.5, 12.5, 12.5, 12.5, 0), default, null);
                Assert.Contains("frameAvgMs=12.5", line);
                Assert.DoesNotContain("12,5", line);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData(null, "unknown")]
        [InlineData("", "unknown")]
        [InlineData("Ar Encounter/1", "Ar_Encounter_1")]
        [InlineData("ok.name-1_x", "ok.name-1_x")]
        public void SanitizeKeepsSafeCharacters(string input, string expected)
        {
            Assert.Equal(expected, PerfLogLine.Sanitize(input));
        }
    }
}
