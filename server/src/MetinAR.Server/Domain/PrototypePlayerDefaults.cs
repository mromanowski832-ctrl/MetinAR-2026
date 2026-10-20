namespace MetinAR.Server.Domain;

/// <summary>
/// Placeholder starting values for a newly created prototype player.
/// These are NOT game-design decisions; they exist only so the API has deterministic data.
/// Override via the <c>PrototypePlayerDefaults</c> configuration section.
/// </summary>
public sealed class PrototypePlayerDefaults
{
    public const string SectionName = "PrototypePlayerDefaults";

    public int Level { get; set; } = 1;
    public int MaxHp { get; set; } = 100;
    public int MaxMp { get; set; } = 100;
    public long Experience { get; set; }
}
