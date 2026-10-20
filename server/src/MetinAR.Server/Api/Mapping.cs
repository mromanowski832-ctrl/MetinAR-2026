using MetinAR.Server.Domain;

namespace MetinAR.Server.Api;

internal static class Mapping
{
    public static PlayerProfileDto ToDto(this PlayerProfile p) => new(
        p.Id,
        p.DisplayName,
        p.Level,
        new ResourcePoolDto(p.CurrentHp, p.MaxHp),
        new ResourcePoolDto(p.CurrentMp, p.MaxMp),
        p.Experience,
        p.LastKnownPosition is { } pos && p.LastKnownPositionAt is { } at
            ? new ReportedPositionDto(pos.Latitude, pos.Longitude, at)
            : null,
        p.CreatedAt,
        p.UpdatedAt);

    public static WorldEntityDto ToDto(this WorldEntity e, DateTimeOffset now, double distanceMeters) => new(
        e.Id,
        e.Type == WorldEntityType.Npc ? "npc" : "mob",
        e.TemplateId,
        e.Latitude,
        e.Longitude,
        e.Level,
        e.Aggressive,
        e.IsActiveAt(now),
        e.SpawnedAt,
        e.DespawnAt,
        Math.Round(distanceMeters, 1));

    public static EncounterSessionDto ToDto(this EncounterSession s) => new(
        s.Id,
        s.PlayerId,
        s.WorldEntityId,
        s.StartedAt,
        s.CompletedAt,
        s.Status == EncounterStatus.Completed ? "completed" : "in_progress");
}
