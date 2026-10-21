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
    DateTimeOffset? ActiveUntil,
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

/// <summary>
/// Client GPS fix used for the server-side proximity check. <c>HorizontalAccuracyMeters</c> is the platform's
/// accuracy radius; <c>CapturedAt</c> (UTC) is optional and, when sent, must be recent.
/// </summary>
public sealed record ClientFixDto(double? Latitude, double? Longitude, double? HorizontalAccuracyMeters, DateTimeOffset? CapturedAt);

public sealed record StartEncounterRequest(string? PlayerId, string? WorldEntityId, ClientFixDto? ClientPosition);

public sealed record CompleteEncounterRequest(string? EncounterId, string? PlayerId, string? Nonce);

public sealed record EncounterSessionDto(
    string Id,
    string PlayerId,
    string WorldEntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiredAt,
    string Status);

/// <summary><c>Nonce</c> is returned only here, once per start call; the server stores only its hash.</summary>
public sealed record StartEncounterResponse(EncounterSessionDto Encounter, bool Created, string Nonce);

public sealed record CompleteEncounterResponse(EncounterSessionDto Encounter, bool FirstCompletion);

public sealed record HealthResponse(string Status, DateTimeOffset ServerTime, string ApiVersion, string? SchemaVersion);
