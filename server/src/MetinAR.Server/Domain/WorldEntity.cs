namespace MetinAR.Server.Domain;

public enum WorldEntityType
{
    Mob,
    Npc,
}

/// <summary>A mob or NPC placed at a geographic location. Ids are stable and server-issued.</summary>
public sealed record WorldEntity(
    string Id,
    WorldEntityType Type,
    string TemplateId,
    double Latitude,
    double Longitude,
    int Level,
    bool Aggressive,
    bool Active,
    DateTimeOffset SpawnedAt,
    DateTimeOffset? DespawnAt,
    SpawnSchedule? Schedule = null)
{
    /// <summary>
    /// Effective availability: the stored flag is on, <paramref name="now"/> lies inside the spawn window
    /// [SpawnedAt, DespawnAt) and, when a schedule exists, inside one of its active phases.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) => ActiveUntil(now) is not null;

    /// <summary>
    /// When the entity is effectively active at <paramref name="now"/>, the instant it stops being active
    /// (<see cref="DateTimeOffset.MaxValue"/> for "no end"); otherwise <c>null</c>.
    /// </summary>
    public DateTimeOffset? ActiveUntil(DateTimeOffset now)
    {
        if (!Active || now < SpawnedAt || (DespawnAt is { } end && now >= end))
        {
            return null;
        }

        DateTimeOffset until = DespawnAt ?? DateTimeOffset.MaxValue;
        if (Schedule is not null)
        {
            (bool active, DateTimeOffset changesAt) = Schedule.Evaluate(now);
            if (!active)
            {
                return null;
            }

            until = changesAt < until ? changesAt : until;
        }

        return until;
    }
}
