using MetinAR.ArEncounter;
using Xunit;

namespace GeoCore.Tests;

/// <summary>
/// #66 "placed mob disappears (mobs 0 spawns 1)": the monster is a child of its AR anchor, and a removed anchor
/// (XR Simulation drops a plane-attached anchor when the plane is merged away) used to destroy it silently.
/// The guard decides to re-home and re-anchor the monster and produces the cause log line.
/// </summary>
public class ArMobAnchorGuardTests
{
    private static ArMobAnchorGuard Anchored(string id = "1A2B3C4D-00000000DEADBEEF", string kind = "plane")
    {
        var g = new ArMobAnchorGuard();
        g.OnAnchorAttached(id, kind);
        return g;
    }

    [Fact]
    public void PlaneAnchorRemovedWhilePlaced_KeepsMonsterAndReanchors()
    {
        ArMobAnchorGuard g = Anchored();
        Assert.Equal(ArAnchorLossAction.RehomeAndReanchor, g.OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", mobAlive: true, exiting: false));
        Assert.Equal(1, g.Removals);
        Assert.Equal(1, g.ReanchorAttempts);
        Assert.Null(g.AnchorId);
        Assert.Equal("session", g.AnchorKind);
    }

    [Fact]
    public void OtherAnchorsRemoved_AreIgnored()
    {
        ArMobAnchorGuard g = Anchored();
        Assert.Equal(ArAnchorLossAction.Ignore, g.OnAnchorRemoved("FFFF-0000", true, false));
        Assert.Equal(0, g.Removals);
        Assert.Equal("plane", g.AnchorKind);
    }

    [Fact]
    public void ExitingOrNoMonsterOrNoAnchor_IsIgnored()
    {
        Assert.Equal(ArAnchorLossAction.Ignore, Anchored().OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", true, exiting: true));
        Assert.Equal(ArAnchorLossAction.Ignore, Anchored().OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", mobAlive: false, exiting: false));
        Assert.Equal(ArAnchorLossAction.Ignore, new ArMobAnchorGuard().OnAnchorRemoved("x", true, false));
    }

    [Fact]
    public void SameRemovalReportedTwice_HandledOnce()
    {
        ArMobAnchorGuard g = Anchored();
        g.OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", true, false);
        Assert.Equal(ArAnchorLossAction.Ignore, g.OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", true, false));
        Assert.Equal(1, g.Removals);
    }

    [Fact]
    public void RepeatedLosses_StopReanchoringAfterLimitButStillKeepMonster()
    {
        var g = new ArMobAnchorGuard();
        for (int i = 0; i < ArMobAnchorGuard.MaxReanchorAttempts; i++)
        {
            g.OnAnchorAttached("a" + i, "world");
            Assert.Equal(ArAnchorLossAction.RehomeAndReanchor, g.OnAnchorRemoved("a" + i, true, false));
        }

        g.OnAnchorAttached("last", "world");
        Assert.Equal(ArAnchorLossAction.RehomeToSession, g.OnAnchorRemoved("last", true, false));
        Assert.Equal(ArMobAnchorGuard.MaxReanchorAttempts + 1, g.Removals);
    }

    [Fact]
    public void RemovedLogLine_NamesKindActionAndKeptMonster()
    {
        ArMobAnchorGuard g = Anchored();
        ArAnchorLossAction a = g.OnAnchorRemoved("1A2B3C4D-00000000DEADBEEF", true, false);
        string line = g.FormatRemovedLog("1A2B3C4D-00000000DEADBEEF", "plane", a, 0.1, 0, 2.5);
        Assert.StartsWith("[ArAnchor] event=anchorRemoved kind=plane id=DEADBEEF action=RehomeAndReanchor reanchor=1/3 removals=1", line);
        Assert.Contains("mob=(0.10, 0.00, 2.50) mobKept=true", line);
    }

    [Fact]
    public void MobDestroyedWhilePlaced_IsUnexpected_ExitIsExpected()
    {
        Assert.Equal("UNEXPECTED", ArMobAnchorGuard.MobDestroyedVerdict(exiting: false, placed: true));
        Assert.Equal("expected", ArMobAnchorGuard.MobDestroyedVerdict(exiting: true, placed: true));
        Assert.Equal("expected", ArMobAnchorGuard.MobDestroyedVerdict(exiting: false, placed: false));
        Assert.Equal("[ArAnchor] event=mobDestroyed verdict=UNEXPECTED kind=plane parent=Anchor placed=true exiting=false spawns=1",
            ArMobAnchorGuard.FormatMobDestroyedLog(false, true, "plane", "Anchor", 1));
    }
}
