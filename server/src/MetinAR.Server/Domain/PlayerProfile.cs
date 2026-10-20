namespace MetinAR.Server.Domain;

/// <summary>
/// Technical prototype player record. Stat fields are server-owned; the client can only
/// change <see cref="DisplayName"/> and report a coarse <see cref="LastKnownPosition"/>.
/// No classes, currency, skills, loot or monetization are modelled on purpose.
/// </summary>
public sealed record PlayerProfile(
    string Id,
    string DisplayName,
    int Level,
    int CurrentHp,
    int MaxHp,
    int CurrentMp,
    int MaxMp,
    long Experience,
    GeoPoint? LastKnownPosition,
    DateTimeOffset? LastKnownPositionAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
