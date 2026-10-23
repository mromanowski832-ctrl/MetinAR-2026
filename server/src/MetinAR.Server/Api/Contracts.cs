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

// ---- SaveGame (METINAR-035 / #71). Document shape: SaveGames/SaveGameDocument.cs = docs/contracts/savegame.v1.schema.json.

/// <summary>GET/PUT savegame response. The same revision is sent as the strong ETag <c>"&lt;revision&gt;"</c>.</summary>
public sealed record SaveGameResponse(long Revision, MetinAR.Server.SaveGames.SaveGameDocument SaveGame, DateTimeOffset ServerTime);

public sealed record RewardClaimStackDto(string? DefinitionId, int? Quantity);

public sealed record RewardClaimItemDto(string? DefinitionId, int? ItemLevel, string? Rarity);

/// <summary>What the client computed for the encounter. A proposal only: the server validates and records it once.</summary>
public sealed record RewardClaimDto(
    long? Exp,
    long? Talers,
    IReadOnlyList<RewardClaimStackDto>? Stacks,
    IReadOnlyList<RewardClaimItemDto>? Items,
    IReadOnlyList<RewardClaimStackDto>? Materials);

public sealed record ClaimEncounterRewardRequest(string? EncounterId, string? PlayerId, string? Nonce, RewardClaimDto? Claim);

/// <summary>
/// <c>applied</c>: this call applied the reward. <c>alreadyApplied</c>: an earlier call did (idempotent replay);
/// <c>claimMatchesApplied</c> tells whether this claim equals the recorded one. <c>granted</c> is what was applied.
/// </summary>
public sealed record ClaimEncounterRewardResponse(
    bool Applied,
    bool AlreadyApplied,
    bool ClaimMatchesApplied,
    MetinAR.Server.SaveGames.SaveRewardGrant Granted,
    long Revision,
    MetinAR.Server.SaveGames.SaveGameDocument SaveGame);
