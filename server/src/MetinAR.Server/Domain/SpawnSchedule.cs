namespace MetinAR.Server.Domain;

/// <summary>
/// Deterministic, server-owned repeating activity window. Starting at <see cref="AnchorAt"/>, every cycle of
/// <see cref="PeriodSeconds"/> begins with <see cref="ActiveSeconds"/> of activity followed by inactivity.
/// No randomness: the same instant always yields the same answer. Values are technical prototype data,
/// not spawn density or balance decisions.
/// </summary>
public sealed record SpawnSchedule(DateTimeOffset AnchorAt, int PeriodSeconds, int ActiveSeconds)
{
    public static bool IsValid(int periodSeconds, int activeSeconds) =>
        periodSeconds > 0 && activeSeconds > 0 && activeSeconds <= periodSeconds;

    /// <summary>The window state at <paramref name="now"/>.</summary>
    /// <returns>
    /// <c>Active</c>; when active, <c>ChangesAt</c> is the end of the current active phase, otherwise the start
    /// of the next active phase.
    /// </returns>
    public (bool Active, DateTimeOffset ChangesAt) Evaluate(DateTimeOffset now)
    {
        if (now < AnchorAt)
        {
            return (false, AnchorAt);
        }

        long periodTicks = PeriodSeconds * TimeSpan.TicksPerSecond;
        long activeTicks = ActiveSeconds * TimeSpan.TicksPerSecond;
        long phase = (now - AnchorAt).Ticks % periodTicks;
        DateTimeOffset cycleStart = now.AddTicks(-phase);
        return phase < activeTicks
            ? (true, cycleStart.AddTicks(activeTicks))
            : (false, cycleStart.AddTicks(periodTicks));
    }
}
