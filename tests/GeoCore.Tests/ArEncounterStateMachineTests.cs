using System.Collections.Generic;
using MetinAR.ArEncounter;
using Xunit;

namespace GeoCore.Tests;

public class ArEncounterStateMachineTests
{
    private static ArPlaneObservation Ground(string id = "p1", float w = 1.0f, float l = 1.0f) =>
        new ArPlaneObservation(id, isHorizontalUp: true, isTracking: true, widthMeters: w, lengthMeters: l, isSubsumed: false);

    private static ArEncounterStateMachine Tracking()
    {
        var sm = new ArEncounterStateMachine();
        sm.SetSessionStatus(ArSessionStatus.CheckingAvailability);
        sm.SetSessionStatus(ArSessionStatus.SessionInitializing);
        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        return sm;
    }

    [Fact]
    public void StartsInStartingWithNothingPlaced()
    {
        var sm = new ArEncounterStateMachine();
        Assert.Equal(ArEncounterState.Starting, sm.State);
        Assert.Equal(0, sm.PlacementCount);
        Assert.Null(sm.PlacedPlaneId);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));
    }

    [Theory]
    [InlineData(ArSessionStatus.None)]
    [InlineData(ArSessionStatus.CheckingAvailability)]
    [InlineData(ArSessionStatus.NeedsInstall)]
    [InlineData(ArSessionStatus.Installing)]
    [InlineData(ArSessionStatus.Ready)]
    public void NotRunningSessionStaysStarting(ArSessionStatus status)
    {
        var sm = new ArEncounterStateMachine();
        sm.SetSessionStatus(status);
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArEncounterState.Starting, sm.State);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));
    }

    [Fact]
    public void RunningSessionScansUntilSuitablePlane()
    {
        var sm = new ArEncounterStateMachine();
        sm.SetSessionStatus(ArSessionStatus.SessionInitializing);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        // Planes are ignored until the session tracks.
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArEncounterState.Scanning, sm.State);

        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        sm.UpdatePlanes(new ArPlaneObservation[0]);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));

        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArEncounterState.ReadyToPlace, sm.State);
        Assert.Contains("p1", sm.SuitablePlaneIds);
    }

    [Fact]
    public void UnsuitablePlanesDoNotMakeReady()
    {
        var sm = Tracking();
        var planes = new List<ArPlaneObservation>
        {
            new ArPlaneObservation("vertical", false, true, 2f, 2f, false),
            new ArPlaneObservation("limited", true, false, 2f, 2f, false),
            new ArPlaneObservation("small", true, true, 0.2f, 2f, false),
            new ArPlaneObservation("narrow", true, true, 2f, 0.29f, false),
            new ArPlaneObservation("subsumed", true, true, 2f, 2f, true),
            new ArPlaneObservation("", true, true, 2f, 2f, false),
            new ArPlaneObservation("nan", true, true, float.NaN, 2f, false),
        };
        sm.UpdatePlanes(planes);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        Assert.Empty(sm.SuitablePlaneIds);
        foreach (var p in planes)
        {
            Assert.NotEqual(ArPlacementResult.Placed, sm.TryPlace(p.Id));
        }

        Assert.Equal(0, sm.PlacementCount);
    }

    [Fact]
    public void MinimumExtentIsInclusiveAndConfigurable()
    {
        var sm = new ArEncounterStateMachine(new ArPlacementSettings { minPlaneExtentMeters = 0.5f });
        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        sm.UpdatePlanes(new[] { Ground("a", 0.49f, 1f) });
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        sm.UpdatePlanes(new[] { Ground("a", 0.5f, 0.5f) });
        Assert.Equal(ArEncounterState.ReadyToPlace, sm.State);
    }

    [Fact]
    public void ReadyFallsBackToScanningWhenPlaneLost()
    {
        var sm = Tracking();
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArEncounterState.ReadyToPlace, sm.State);
        sm.UpdatePlanes(new[] { new ArPlaneObservation("p1", true, false, 1f, 1f, false) });
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));
    }

    [Fact]
    public void TrackingLossBeforePlacementReturnsToScanning()
    {
        var sm = Tracking();
        sm.UpdatePlanes(new[] { Ground() });
        sm.SetSessionStatus(ArSessionStatus.SessionInitializing);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        Assert.Empty(sm.SuitablePlaneIds);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));
    }

    [Fact]
    public void PlacesExactlyOnceOnSuitablePlane()
    {
        var sm = Tracking();
        var transitions = new List<(ArEncounterState, ArEncounterState)>();
        sm.StateChanged += (a, b) => transitions.Add((a, b));
        sm.UpdatePlanes(new[] { Ground("floor"), Ground("table", 0.6f, 0.6f) });
        Assert.Equal(ArEncounterState.ReadyToPlace, sm.State);

        Assert.Equal(ArPlacementResult.UnsuitablePlane, sm.TryPlace("unknown"));
        Assert.Equal(ArPlacementResult.UnsuitablePlane, sm.TryPlace(null));
        Assert.Equal(ArEncounterState.ReadyToPlace, sm.State);

        Assert.Equal(ArPlacementResult.Placed, sm.TryPlace("floor"));
        Assert.Equal(ArEncounterState.Placed, sm.State);
        Assert.Equal("floor", sm.PlacedPlaneId);
        Assert.Equal(1, sm.PlacementCount);

        // Repeated taps / confirms never spawn a duplicate.
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(ArPlacementResult.AlreadyPlaced, sm.TryPlace("floor"));
            Assert.Equal(ArPlacementResult.AlreadyPlaced, sm.TryPlace("table"));
        }

        Assert.Equal(1, sm.PlacementCount);
        Assert.Equal(
            new[] { (ArEncounterState.Scanning, ArEncounterState.ReadyToPlace), (ArEncounterState.ReadyToPlace, ArEncounterState.Placed) },
            transitions.ToArray());
    }

    [Fact]
    public void PlacedStaysPlacedUnderTrackingChangesAndPlaneUpdates()
    {
        var sm = Tracking();
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArPlacementResult.Placed, sm.TryPlace("p1"));

        sm.SetSessionStatus(ArSessionStatus.SessionInitializing);
        Assert.Equal(ArEncounterState.Placed, sm.State);
        Assert.True(sm.IsTrackingLimited);
        sm.UpdatePlanes(new ArPlaneObservation[0]);
        sm.Tick(1000f);
        sm.Fail("late failure");
        sm.SetSessionStatus(ArSessionStatus.Unsupported);
        Assert.Equal(ArEncounterState.Placed, sm.State);

        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        Assert.False(sm.IsTrackingLimited);
        Assert.Equal(ArEncounterState.Placed, sm.State);
        Assert.Equal(1, sm.PlacementCount);
        Assert.Equal(ArPlacementResult.AlreadyPlaced, sm.TryPlace("p1"));
    }

    [Fact]
    public void UnsupportedIsTerminalAndNeverPlaces()
    {
        var sm = new ArEncounterStateMachine();
        sm.SetSessionStatus(ArSessionStatus.CheckingAvailability);
        sm.SetSessionStatus(ArSessionStatus.Unsupported);
        Assert.Equal(ArEncounterState.Unsupported, sm.State);
        Assert.True(sm.IsTerminalFailure);
        Assert.False(string.IsNullOrEmpty(sm.FailureReason));

        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        sm.UpdatePlanes(new[] { Ground() });
        sm.Tick(100f);
        Assert.Equal(ArEncounterState.Unsupported, sm.State);
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));
        Assert.Equal(0, sm.PlacementCount);
    }

    [Fact]
    public void StartTimeoutFailsAndLaterSessionStartRecovers()
    {
        var sm = new ArEncounterStateMachine(new ArPlacementSettings { startTimeoutSeconds = 5f });
        sm.SetSessionStatus(ArSessionStatus.None);
        sm.Tick(2f);
        sm.Tick(2.9f);
        Assert.Equal(ArEncounterState.Starting, sm.State);
        sm.Tick(0.2f);
        Assert.Equal(ArEncounterState.Failed, sm.State);
        Assert.False(sm.IsTerminalFailure);
        Assert.False(string.IsNullOrEmpty(sm.FailureReason));

        // Still not running: stays failed, cannot place.
        sm.SetSessionStatus(ArSessionStatus.NeedsInstall);
        Assert.Equal(ArEncounterState.Failed, sm.State);
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArPlacementResult.NotReady, sm.TryPlace("p1"));

        // Session eventually starts (e.g. user accepted the install / permission): recovers.
        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
        Assert.Null(sm.FailureReason);
        sm.UpdatePlanes(new[] { Ground() });
        Assert.Equal(ArPlacementResult.Placed, sm.TryPlace("p1"));
    }

    [Fact]
    public void TimeoutOnlyCountsWhileStartingAndIgnoresBadDeltas()
    {
        var sm = new ArEncounterStateMachine(new ArPlacementSettings { startTimeoutSeconds = 1f });
        sm.Tick(float.NaN);
        sm.Tick(float.PositiveInfinity);
        sm.Tick(-5f);
        Assert.Equal(ArEncounterState.Starting, sm.State);

        sm.SetSessionStatus(ArSessionStatus.SessionTracking);
        sm.Tick(100f);
        Assert.Equal(ArEncounterState.Scanning, sm.State);

        // Dropping back to not-running restarts the timer from zero.
        sm.SetSessionStatus(ArSessionStatus.Ready);
        Assert.Equal(ArEncounterState.Starting, sm.State);
        sm.Tick(0.9f);
        Assert.Equal(ArEncounterState.Starting, sm.State);
        sm.Tick(0.2f);
        Assert.Equal(ArEncounterState.Failed, sm.State);
    }

    [Fact]
    public void ExplicitFailureIsRecoverable()
    {
        var sm = new ArEncounterStateMachine();
        sm.Fail("Brak aktywnego dostawcy XR.");
        Assert.Equal(ArEncounterState.Failed, sm.State);
        Assert.Equal("Brak aktywnego dostawcy XR.", sm.FailureReason);
        sm.SetSessionStatus(ArSessionStatus.SessionInitializing);
        Assert.Equal(ArEncounterState.Scanning, sm.State);
    }

    [Fact]
    public void InvalidSettingsAreRejected()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterStateMachine(new ArPlacementSettings { minPlaneExtentMeters = -1f }));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterStateMachine(new ArPlacementSettings { minPlaneExtentMeters = float.NaN }));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterStateMachine(new ArPlacementSettings { startTimeoutSeconds = 0f }));
    }
}

public class ArEncounterRequestTests
{
    [Fact]
    public void PrototypeTestSpiderMatchesOwnerConfirmedPrototypeMob()
    {
        var r = ArEncounterRequest.PrototypeTestSpider("direct");
        Assert.Equal("Trujący Pająk", r.MobName);
        Assert.Equal(5, r.Level);
        Assert.Equal(120, r.CurrentHp);
        Assert.Equal(120, r.MaxHp);
        Assert.True(r.Aggressive);
        Assert.Equal("direct", r.Reason);
        Assert.Equal("Poz. 5", r.LevelLabel);
        Assert.Equal("HP 120/120", r.HpLabel);
        Assert.Equal(1f, r.HpFraction);
    }

    [Fact]
    public void InvalidRequestsAreRejected()
    {
        Assert.Throws<System.ArgumentException>(() => new ArEncounterRequest(" ", 5, 120, 120, true, "x"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterRequest("a", 0, 1, 1, true, "x"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterRequest("a", 1, 1, 0, true, "x"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterRequest("a", 1, 2, 1, true, "x"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ArEncounterRequest("a", 1, -1, 1, true, "x"));
        Assert.Equal(string.Empty, new ArEncounterRequest("a", 1, 1, 1, false, null).Reason);
    }

    [Fact]
    public void HpFractionReflectsCurrentHp()
    {
        Assert.Equal(0.5f, new ArEncounterRequest("a", 1, 60, 120, true, "x").HpFraction);
    }
}

[Collection("ArEncounterHandoff")]
public class ArEncounterHandoffTests
{
    [Fact]
    public void SubmitConsumeOnce()
    {
        ArEncounterHandoff.Reset();
        Assert.False(ArEncounterHandoff.HasPending);
        Assert.False(ArEncounterHandoff.TryConsume(out var none));
        Assert.Null(none);

        var first = ArEncounterRequest.PrototypeTestSpider("panel:Trujący Pająk");
        var second = ArEncounterRequest.PrototypeTestSpider("aggro:Trujący Pająk");
        ArEncounterHandoff.Submit(first);
        ArEncounterHandoff.Submit(second);
        Assert.Equal(2, ArEncounterHandoff.SubmittedCount);
        Assert.Same(second, ArEncounterHandoff.Pending);

        Assert.True(ArEncounterHandoff.TryConsume(out var got));
        Assert.Same(second, got);
        Assert.False(ArEncounterHandoff.TryConsume(out _));

        ArEncounterHandoff.Submit(first);
        ArEncounterHandoff.Clear();
        Assert.False(ArEncounterHandoff.HasPending);
        Assert.Equal(3, ArEncounterHandoff.SubmittedCount);
        Assert.Throws<System.ArgumentNullException>(() => ArEncounterHandoff.Submit(null));
        ArEncounterHandoff.Reset();
        Assert.Equal(0, ArEncounterHandoff.SubmittedCount);
    }
}
