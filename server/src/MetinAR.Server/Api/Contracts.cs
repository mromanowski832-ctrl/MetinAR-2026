namespace MetinAR.Server.Api;

// Wire contract for /api/v1. These records are the only shapes the Unity client sees.
// Documented in docs/METINAR-010-BACKEND.md; keep both in sync.

public sealed record GeoPositionDto(double Latitude, double Longitude);

public sealed record ReportedPositionDto(double Latitude, double Longitude, DateTimeOffset ReportedAt);

public sealed record ResourcePoolDto(int Current, int Max);

public sealed record PlayerProfileDto(
    string Id,
    string DisplayName,
    int Level,
    ResourcePoolDto Hp,
    ResourcePoolDto Mp,
    long Experience,
    ReportedPositionDto? LastKnownPosition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Only client-editable fields. Server-owned stats are rejected if sent.</summary>
public sealed record PutPlayerRequest(string? DisplayName, GeoPositionDto? LastKnownPosition);

public sealed record WorldEntityDto(
    string Id,
    string Type,
    string TemplateId,
    double Latitude,
    double Longitude,
    int Level,
    bool Aggressive,
    bool Active,
    DateTimeOffset SpawnedAt,
    DateTimeOffset? DespawnAt,
    double DistanceMeters);

public sealed record NearbyWorldResponse(
    GeoPositionDto Center,
    double RadiusMeters,
    DateTimeOffset ServerTime,
    int Offset,
    int Limit,
    int TotalInRadius,
    int? NextOffset,
    IReadOnlyList<WorldEntityDto> Entities);

public sealed record StartEncounterRequest(string? PlayerId, string? WorldEntityId, GeoPositionDto? ClientPosition);

public sealed record CompleteEncounterRequest(string? EncounterId, string? PlayerId);

public sealed record EncounterSessionDto(
    string Id,
    string PlayerId,
    string WorldEntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status);

public sealed record StartEncounterResponse(EncounterSessionDto Encounter, bool Created);

public sealed record CompleteEncounterResponse(EncounterSessionDto Encounter, bool FirstCompletion);

public sealed record HealthResponse(string Status, DateTimeOffset ServerTime, string ApiVersion, string? SchemaVersion);
