using MetinAR.WorldMap;
using Xunit;

namespace GeoCore.Tests;

public class AggroConfidenceGateTests
{
    private const double Range = 12.0;

    private static AggroConfidenceGate NewGate() => new AggroConfidenceGate(AggroGateSettings.CreatePrototypeDefaults());

    /// <summary>Feeds fixes 1 s apart starting at t0; returns trigger count.</summary>
    private static int Feed(AggroConfidenceGate gate, ref double t, params (double dist, double acc)[] fixes)
    {
        int triggers = 0;
        foreach (var (dist, acc) in fixes)
        {
            if (gate.Submit(dist, acc, t, Range))
            {
                triggers++;
            }

            t += 1.0;
        }

        return triggers;
    }

    [Fact]
    public void DefaultsAreDocumentedPrototypeValues()
    {
        var s = AggroGateSettings.CreatePrototypeDefaults();
        Assert.Equal(10.0, s.maxHorizontalAccuracyMeters);
        Assert.Equal(1.6, s.accuracyMarginFactor);
        Assert.Equal(3, s.requiredStableFixes);
        Assert.Equal(2.0, s.minStableSeconds);
        Assert.Equal(10.0, s.maxFixGapSeconds);
        Assert.Equal(6.0, s.rearmExtraMeters);
        Assert.Equal(2, s.requiredRearmFixes);
        Assert.Equal(8.0, s.minRetriggerSeconds);
    }

    [Fact]
    public void StationaryJitterAroundBoundaryDoesNotTrigger()
    {
        // Player stands ~13 m from the mob; estimates wander 10.5–15.5 m with 2–6 m accuracy
        // (raw distance often <= 12 m, but the accuracy circle never lies inside the range).
        var gate = NewGate();
        double t = 100.0;
        var jitter = new (double, double)[]
        {
            (13.2, 3.0), (11.5, 2.0), (10.8, 4.0), (12.4, 1.7), (11.0, 5.8),
            (10.5, 3.5), (14.1, 2.5), (11.9, 2.0), (10.9, 3.0), (15.5, 6.0),
        };
        int triggers = 0;
        for (int round = 0; round < 6; round++)
        {
            triggers += Feed(gate, ref t, jitter);
        }

        Assert.Equal(0, triggers);
        Assert.True(gate.IsArmed);
    }

    [Fact]
    public void JitterOutsideBoundaryDoesNotTrigger()
    {
        var gate = NewGate();
        double t = 0.0;
        int triggers = Feed(gate, ref t,
            (20.0, 3.0), (18.5, 2.0), (22.0, 4.0), (19.0, 1.7), (21.3, 5.0), (17.0, 2.0));
        Assert.Equal(0, triggers);
    }

    [Fact]
    public void PoorAccuracyFixesDoNotTriggerEvenWhenEstimateIsVeryClose()
    {
        // Early/indoor fixes: estimate jumps right onto the mob but accuracy is 11–19.8 m.
        var gate = NewGate();
        double t = 0.0;
        int triggers = Feed(gate, ref t,
            (0.5, 19.8), (1.0, 15.0), (2.0, 12.0), (0.0, 11.0), (1.5, 18.0), (3.0, 10.5), (0.2, 14.0));
        Assert.Equal(0, triggers);
        Assert.Equal(0, gate.StableInsideCount);
    }

    [Fact]
    public void PoorAccuracyFixBreaksTheStabilityStreak()
    {
        var gate = NewGate();
        double t = 0.0;
        int triggers = Feed(gate, ref t, (3.0, 2.0), (3.0, 2.0), (3.0, 25.0), (3.0, 2.0), (3.0, 2.0));
        Assert.Equal(0, triggers);
        Assert.Equal(2, gate.StableInsideCount);
    }

    [Fact]
    public void SingleCloseFixDoesNotTrigger()
    {
        var gate = NewGate();
        double t = 0.0;
        int triggers = Feed(gate, ref t, (14.0, 3.0), (4.0, 3.0), (14.0, 3.0), (13.0, 3.0));
        Assert.Equal(0, triggers);
    }

    [Fact]
    public void StableHighConfidenceApproachTriggersOnce()
    {
        var gate = NewGate();
        double t = 0.0;
        // Walk in from 30 m, then stand next to the mob for a while.
        int walkIn = Feed(gate, ref t, (30.0, 3.0), (24.0, 3.0), (18.0, 2.5), (12.0, 2.5), (8.0, 2.0), (6.0, 2.0));
        int stay = Feed(gate, ref t, (5.0, 2.0), (5.5, 3.0), (4.5, 2.0), (6.0, 2.5), (5.0, 2.0), (5.2, 1.7),
            (4.8, 2.0), (5.1, 2.0), (5.0, 3.0), (5.0, 2.0), (4.9, 2.0), (5.3, 2.0));
        Assert.Equal(1, walkIn + stay);
        Assert.Equal(1, gate.TriggerCount);
        Assert.False(gate.IsArmed);
    }

    [Fact]
    public void ApproachTriggersOnThirdStableFixNotBefore()
    {
        var gate = NewGate();
        Assert.False(gate.Submit(6.0, 2.0, 10.0, Range));
        Assert.False(gate.Submit(6.0, 2.0, 11.0, Range));
        Assert.True(gate.Submit(6.0, 2.0, 12.0, Range));
    }

    [Fact]
    public void RepeatedSameTimestampIsCountedOnce()
    {
        // The mob polls the latest fix every frame; one fix must count once.
        var gate = NewGate();
        for (int frame = 0; frame < 100; frame++)
        {
            Assert.False(gate.Submit(5.0, 2.0, 50.0, Range));
        }

        Assert.Equal(1, gate.StableInsideCount);
    }

    [Fact]
    public void FastFixesMustStillSpanMinimumStableTime()
    {
        var gate = NewGate();
        Assert.False(gate.Submit(5.0, 2.0, 0.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 0.5, Range));
        Assert.False(gate.Submit(5.0, 2.0, 1.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 1.5, Range));
        Assert.True(gate.Submit(5.0, 2.0, 2.0, Range));
    }

    [Fact]
    public void BoundaryOscillationAfterTriggerDoesNotRepeat()
    {
        var gate = NewGate();
        double t = 0.0;
        Assert.Equal(1, Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0)));
        // Oscillate in and out of the range for two minutes (never confidently beyond 18 m).
        int repeats = 0;
        for (int i = 0; i < 60; i++)
        {
            repeats += Feed(gate, ref t, (8.0, 2.0), (14.0, 2.0), (17.0, 3.0), (9.0, 1.7));
        }

        Assert.Equal(0, repeats);
        Assert.False(gate.IsArmed);
    }

    [Fact]
    public void BoundaryOscillationBeforeTriggerDoesNotTrigger()
    {
        var gate = NewGate();
        double t = 0.0;
        int triggers = 0;
        for (int i = 0; i < 60; i++)
        {
            triggers += Feed(gate, ref t, (6.0, 2.0), (13.0, 2.0), (5.0, 2.0), (12.5, 2.0));
        }

        Assert.Equal(0, triggers);
    }

    [Fact]
    public void LeavingFarAndReturningReArms()
    {
        var gate = NewGate();
        double t = 0.0;
        Assert.Equal(1, Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0)));
        // Walk away to ~25 m (confidently beyond 12 + 6 m).
        Assert.Equal(0, Feed(gate, ref t, (10.0, 2.0), (16.0, 2.0), (22.0, 2.0), (25.0, 3.0)));
        Assert.True(gate.IsArmed);
        // Return and stay close: triggers again exactly once.
        int again = Feed(gate, ref t, (18.0, 2.0), (12.0, 2.0), (7.0, 2.0), (6.0, 2.0), (5.0, 2.0), (5.0, 2.0), (5.0, 2.0));
        Assert.Equal(1, again);
        Assert.Equal(2, gate.TriggerCount);
    }

    [Fact]
    public void SingleFarFixDoesNotReArm()
    {
        var gate = NewGate();
        double t = 0.0;
        Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0));
        Feed(gate, ref t, (30.0, 2.0), (10.0, 2.0));
        Assert.False(gate.IsArmed);
    }

    [Fact]
    public void FarFixWithPoorAccuracyDoesNotReArm()
    {
        var gate = NewGate();
        double t = 0.0;
        Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0));
        // 22 m estimate but 5 m accuracy -> 17 m < 18 m; and 40 m with 15 m accuracy is ignored.
        Feed(gate, ref t, (22.0, 5.0), (22.0, 5.0), (40.0, 15.0), (40.0, 15.0));
        Assert.False(gate.IsArmed);
    }

    [Fact]
    public void MinRetriggerTimeIsRespectedAfterQuickReArm()
    {
        var settings = AggroGateSettings.CreatePrototypeDefaults();
        settings.minRetriggerSeconds = 20.0;
        var gate = new AggroConfidenceGate(settings);
        double t = 0.0;
        Assert.Equal(1, Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0))); // trigger at t=2
        Feed(gate, ref t, (25.0, 2.0), (25.0, 2.0)); // re-armed at t=4
        Assert.True(gate.IsArmed);
        int blocked = Feed(gate, ref t, (5.0, 2.0), (5.0, 2.0), (5.0, 2.0), (5.0, 2.0)); // t=5..8
        Assert.Equal(0, blocked);
        // Keep standing close: fires on the first stable fix at/after t = 2 + 20 = 22.
        for (; t < 22.0; t += 1.0)
        {
            Assert.False(gate.Submit(5.0, 2.0, t, Range));
        }

        Assert.True(gate.Submit(5.0, 2.0, 22.0, Range));
    }

    [Fact]
    public void LargeGapResetsStabilityHistory()
    {
        // e.g. app paused between fixes: stale inside fixes must not combine with new ones.
        var gate = NewGate();
        Assert.False(gate.Submit(5.0, 2.0, 0.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 1.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 30.0, Range));
        Assert.Equal(1, gate.StableInsideCount);
        Assert.False(gate.Submit(5.0, 2.0, 31.0, Range));
        Assert.True(gate.Submit(5.0, 2.0, 32.0, Range));
    }

    [Fact]
    public void BackwardTimestampStartsNewStream()
    {
        var gate = NewGate();
        Assert.False(gate.Submit(5.0, 2.0, 100.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 101.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, 1.0, Range));
        Assert.Equal(1, gate.StableInsideCount);
        Assert.False(gate.Submit(5.0, 2.0, 2.0, Range));
        Assert.True(gate.Submit(5.0, 2.0, 3.0, Range));
    }

    [Fact]
    public void RecordedGalaxyA31StationaryStartDoesNotTrigger()
    {
        // METINAR-005 physical log (relative values only: seconds since first fix, metres to the mob, accuracy).
        // At ~10 s the estimate jumped ~11 m onto the mob and stayed there ~40 s with 2.4–5.7 m reported accuracy
        // while the owner stood still ~13 m away. The old raw-distance check attacked repeatedly here.
        var fixes = new (double t, double mob, double acc)[]
        {
            (0.0, 13.15, 3.0), (6.0, 19.24, 3.9), (8.0, 16.58, 4.5), (10.0, 5.01, 5.1), (12.1, 5.01, 5.0),
            (14.1, 5.01, 5.7), (42.2, 5.01, 4.5), (48.2, 5.01, 4.6), (52.3, 5.01, 2.4), (58.3, 19.96, 1.8),
            (60.3, 20.08, 1.8), (74.4, 20.43, 1.8),
        };
        var gate = NewGate();
        int triggers = 0;
        foreach (var (t, mob, acc) in fixes)
        {
            if (gate.Submit(mob, acc, t, Range))
            {
                triggers++;
            }
        }

        Assert.Equal(0, triggers);
    }

    [Fact]
    public void InvalidInputIsIgnored()
    {
        var gate = NewGate();
        Assert.False(gate.Submit(double.NaN, 2.0, 0.0, Range));
        Assert.False(gate.Submit(5.0, double.PositiveInfinity, 1.0, Range));
        Assert.False(gate.Submit(5.0, 2.0, double.NaN, Range));
        Assert.False(gate.Submit(5.0, 2.0, 2.0, 0.0));
        Assert.False(gate.Submit(-1.0, 2.0, 3.0, Range));
        Assert.Equal(0, gate.StableInsideCount);
    }

    [Fact]
    public void ResetReArmsAndClearsHistory()
    {
        var gate = NewGate();
        double t = 0.0;
        Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0));
        Assert.False(gate.IsArmed);
        gate.Reset();
        Assert.True(gate.IsArmed);
        Assert.Equal(0, gate.StableInsideCount);
        Assert.Equal(1, Feed(gate, ref t, (6.0, 2.0), (6.0, 2.0), (6.0, 2.0)));
    }

    [Fact]
    public void NullSettingsThrow()
    {
        Assert.Throws<System.ArgumentNullException>(() => new AggroConfidenceGate(null));
    }
}
