namespace MetinAR.Server.SaveGames;

/// <summary>
/// Starting values of a player's first SaveGame. PLACEHOLDERS, NOT game-design decisions (#71 open questions):
/// class choice has no UI yet (the S2 client is a warrior), capacity 24 is the ITEMS_COMBAT_SPEC §5 proposal and the
/// starter sword matches the client's StarterLoadout. Override via the <c>PrototypeSaveGameDefaults</c> section.
/// </summary>
public sealed class PrototypeSaveGameDefaults
{
    public const string SectionName = "PrototypeSaveGameDefaults";

    public string ClassId { get; set; } = "warrior";

    public int InventoryCapacity { get; set; } = 24;

    /// <summary>Item definition equipped in the weapon slot of a new save; empty for none.</summary>
    public string StarterWeaponDefinitionId { get; set; } = "starter_sword";
}

/// <summary>
/// Technical limits for SaveGame requests and PROTOTYPE anti-abuse caps for client reward claims. The caps are not
/// game balance: the real reward calculation moves to the server with S11 (#56); until then a claim inside the caps is
/// accepted as a proposal and recorded once per encounter.
/// </summary>
public sealed class SaveGameLimits
{
    public const string SectionName = "SaveGameLimits";

    public int MaxBodyBytes { get; set; } = 1_048_576;

    public int MaxEncounterResultsInDocument { get; set; } = 200;

    public long MaxRewardExpPerEncounter { get; set; } = 100_000;

    public long MaxRewardTalersPerEncounter { get; set; } = 100_000;

    public int MaxRewardEntriesPerList { get; set; } = 10;

    public int MaxRewardQuantityPerEntry { get; set; } = 99;

    public IEnumerable<string> Validate()
    {
        if (MaxBodyBytes < 1024)
        {
            yield return "MaxBodyBytes must be >= 1024.";
        }

        if (MaxEncounterResultsInDocument is < 1 or > SaveGameRules.SchemaMaxEncounterResults)
        {
            yield return $"MaxEncounterResultsInDocument must be 1-{SaveGameRules.SchemaMaxEncounterResults}.";
        }

        if (MaxRewardExpPerEncounter < 0 || MaxRewardTalersPerEncounter < 0)
        {
            yield return "Reward caps must be >= 0.";
        }

        if (MaxRewardEntriesPerList is < 0 or > SaveGameRules.SchemaMaxRewardEntries)
        {
            yield return $"MaxRewardEntriesPerList must be 0-{SaveGameRules.SchemaMaxRewardEntries}.";
        }

        if (MaxRewardQuantityPerEntry < 1)
        {
            yield return "MaxRewardQuantityPerEntry must be >= 1.";
        }
    }
}
