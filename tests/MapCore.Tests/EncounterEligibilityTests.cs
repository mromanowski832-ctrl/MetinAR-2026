using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class EncounterEligibilityTests
{
    // Defaults: radius 25 m, max accuracy 10 m, margin 1.6x, exit hysteresis 5 m, 2 stable fixes, max fix age 10 s.

    [Fact]
    public void FarAway_IsTooFar_WithRemainingDistance()
    {
        var g = new EncounterEligibility();
        Assert.Equal(EncounterAvailability.TooFar, g.Update(80, 5, 1, 1, true));
        Assert.Equal(80 + 8 - 25, g.DistanceToEnterMeters, 6);
        Assert.False(g.IsAvailable);
    }

    [Fact]
    public void Approach_BecomesAvailable_AfterTwoDistinctInRangeFixes()
    {
        var g = new EncounterEligibility();
        Assert.Equal(EncounterAvailability.TooFar, g.Update(60, 4, 1, 1, true));
        Assert.Equal(EncounterAvailability.TooFar, g.Update(18, 4, 2, 2, true));   // 18 + 6.4 <= 25, first in-range fix
        Assert.Equal(EncounterAvailability.TooFar, g.Update(18, 4, 2, 2.5, true)); // same fix polled again: no double count
        Assert.Equal(EncounterAvailability.Available, g.Update(17, 4, 3, 3, true));
        Assert.Equal(0, g.DistanceToEnterMeters, 6);
    }

    [Fact]
    public void RawDistanceInsideButAccuracyCircleOutside_IsNotEnough()
    {
        var g = new EncounterEligibility();
        for (int t = 1; t <= 5; t++)
        {
            Assert.Equal(EncounterAvailability.TooFar, g.Update(20, 8, t, t, true)); // 20 + 12.8 > 25
        }
    }

    [Fact]
    public void PoorAccuracy_DisablesEvenWhenClose()
    {
        var g = new EncounterEligibility();
        Assert.Equal(EncounterAvailability.PoorAccuracy, g.Update(2, 15, 1, 1, true));
        Assert.Equal(EncounterAvailability.PoorAccuracy, g.Update(2, 15, 2, 2, true));
        Assert.Equal(EncounterAvailability.TooFar, g.Update(2, 5, 3, 3, true));
        Assert.Equal(EncounterAvailability.Available, g.Update(2, 5, 4, 4, true));
        Assert.Equal(EncounterAvailability.PoorAccuracy, g.Update(2, 30, 5, 5, true)); // drops out immediately
    }

    [Fact]
    public void Hysteresis_KeepsButtonThroughSmallJitter_ThenDisablesWhenClearlyOut()
    {
        var g = new EncounterEligibility();
        g.Update(10, 3, 1, 1, true);
        Assert.Equal(EncounterAvailability.Available, g.Update(10, 3, 2, 2, true));
        Assert.Equal(EncounterAvailability.Available, g.Update(28, 3, 3, 3, true));  // 28 - 4.8 = 23.2 <= 30
        Assert.Equal(EncounterAvailability.Available, g.Update(34, 3, 4, 4, true));  // 29.2 <= 30
        Assert.Equal(EncounterAvailability.TooFar, g.Update(36, 3, 5, 5, true));     // 31.2 > 30
        Assert.Equal(EncounterAvailability.TooFar, g.Update(20, 3, 6, 6, true));     // re-entry needs a new streak
        Assert.Equal(EncounterAvailability.Available, g.Update(20, 3, 7, 7, true));
    }

    [Fact]
    public void StaleFix_Disables()
    {
        var g = new EncounterEligibility();
        g.Update(5, 3, 1, 1, true);
        Assert.Equal(EncounterAvailability.Available, g.Update(5, 3, 2, 2, true));
        Assert.Equal(EncounterAvailability.Available, g.Update(5, 3, 2, 11.9, true));
        Assert.Equal(EncounterAvailability.StaleFix, g.Update(5, 3, 2, 12.5, true));
    }

    [Fact]
    public void UnsafeSite_NeverAvailable()
    {
        var g = new EncounterEligibility();
        for (int t = 1; t <= 4; t++)
        {
            Assert.Equal(EncounterAvailability.UnsafeSite, g.Update(1, 2, t, t, false));
        }
    }

    [Theory]
    [InlineData(double.NaN, 3)]
    [InlineData(-1, 3)]
    [InlineData(5, double.PositiveInfinity)]
    public void InvalidInput_IsNone(double d, double acc)
    {
        var g = new EncounterEligibility();
        Assert.Equal(EncounterAvailability.None, g.Update(d, acc, 1, 1, true));
    }

    [Fact]
    public void Ticket_OnlyWhenAvailable_AndCarriesSimulationFlag()
    {
        var g = new EncounterEligibility();
        Assert.False(g.TryCreateTicket("spider-1", "Trujący Pająk", 5, LocationSourceKind.Gps, out var none));
        Assert.Null(none);

        g.Update(10, 3, 1, 1, true);
        g.Update(10, 3, 2, 2, true);
        Assert.True(g.TryCreateTicket("spider-1", "Trujący Pająk", 5, LocationSourceKind.Gps, out var gps));
        Assert.False(gps.IsSimulated);
        Assert.True(gps.ProductionRewardsAllowed);
        Assert.Equal("", gps.RewardTag);
        Assert.Equal(5, gps.Level);

        Assert.True(g.TryCreateTicket("spider-1", "Trujący Pająk", 5, LocationSourceKind.Simulation, out var sim));
        Assert.True(sim.IsSimulated);
        Assert.False(sim.ProductionRewardsAllowed);
        Assert.Equal("SYMULACJA", sim.RewardTag);
        Assert.DoesNotContain("52.", sim.ToString()); // no coordinates in the ticket text
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var g = new EncounterEligibility();
        g.Update(10, 3, 1, 1, true);
        g.Update(10, 3, 2, 2, true);
        g.Reset();
        Assert.Equal(EncounterAvailability.None, g.State);
        Assert.Equal(EncounterAvailability.TooFar, g.Update(10, 3, 3, 3, true));
    }
}
