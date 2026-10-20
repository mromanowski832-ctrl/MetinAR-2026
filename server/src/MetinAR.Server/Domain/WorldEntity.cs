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
    DateTimeOffset? DespawnAt)
{
    /// <summary>
    /// Effective availability: the stored flag is on and <paramref name="now"/> lies inside
    /// the spawn window [SpawnedAt, DespawnAt).
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        Active && SpawnedAt <= now && (DespawnAt is null || now < DespawnAt.Value);
}
