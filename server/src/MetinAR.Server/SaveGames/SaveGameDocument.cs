namespace MetinAR.Server.SaveGames;

// SaveGame v1 wire/storage shape (METINAR-071, #71). Mirrors docs/contracts/savegame.v1.schema.json exactly;
// SaveGameSchemaDriftTests fails when the two diverge. Every member is `required`: a missing field is a 400, and
// unknown fields are rejected by the shared JSON options (UnmappedMemberHandling.Disallow).

public sealed record SaveGameDocument
{
    public required int SchemaVersion { get; init; }

    /// <summary><c>local</c> (client-only, unverified) or <c>server</c> (confirmed by the server at <see cref="Revision"/>).</summary>
    public required string Mode { get; init; }

    public required string PlayerId { get; init; }

    public required long Revision { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required SaveProfile Profile { get; init; }

    public required SaveCharacter Character { get; init; }

    public required SaveStats Stats { get; init; }

    public required SaveSkills Skills { get; init; }

    public required SaveInventory Inventory { get; init; }

    public required IReadOnlyList<SaveEquippedItem> Equipment { get; init; }

    public required SaveCurrency Currency { get; init; }

    public required IReadOnlyList<SaveQuestState> Quests { get; init; }

    public required SaveExploration Exploration { get; init; }

    public required IReadOnlyList<SaveEncounterResult> EncounterResults { get; init; }
}

public sealed record SaveProfile
{
    public required string DisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record SaveCharacter
{
    public required string ClassId { get; init; }

    public required string? PathId { get; init; }

    public required int Level { get; init; }

    public required long TotalExp { get; init; }

    public required int HpCurrent { get; init; }

    public required int MpCurrent { get; init; }
}

public sealed record SaveStats
{
    public required SavePrimaryStats Allocated { get; init; }

    public required int FreeStatPoints { get; init; }
}

public sealed record SavePrimaryStats
{
    public required int Might { get; init; }

    public required int Finesse { get; init; }

    public required int Spirit { get; init; }

    public required int Vigor { get; init; }
}

public sealed record SaveSkills
{
    public required int FreeSkillPoints { get; init; }

    public required IReadOnlyList<SaveSkillProgress> Progress { get; init; }

    public required SaveSkillLoadout Loadout { get; init; }
}

public sealed record SaveSkillProgress
{
    public required string SkillId { get; init; }

    public required int RankIndex { get; init; }

    public required string TierId { get; init; }

    public required int TierRank { get; init; }

    public required int PointsSpent { get; init; }

    public required int AttemptsTotal { get; init; }

    public required DateTimeOffset? LastAttemptAtUtc { get; init; }

    public required DateTimeOffset? NextAttemptAtUtc { get; init; }
}

public sealed record SaveSkillLoadout
{
    public required string? Attack { get; init; }

    public required string? Aura { get; init; }

    public required string? Slot1 { get; init; }

    public required string? Slot2 { get; init; }
}

public sealed record SaveInventory
{
    public required int Capacity { get; init; }

    public required IReadOnlyList<SaveSlottedStackEntry> Stacks { get; init; }

    public required IReadOnlyList<SaveSlottedItem> Items { get; init; }

    public required SaveLootStash LootStash { get; init; }
}

/// <summary>
/// Overflow for loot that did not fit the bag (ITEMS_COMBAT_SPEC §5: 8 places, 24 h). Every entry takes one place and
/// expires at <c>expiresAtUtc</c>; what happens after that is configurable (<see cref="LootStashOptions"/>, default keep; #71 §11.7, owner decision open). Only the server adds entries.
/// </summary>
public sealed record SaveLootStash
{
    public required IReadOnlyList<SaveStashStackEntry> Stacks { get; init; }

    public required IReadOnlyList<SaveStashItem> Items { get; init; }
}

public sealed record SaveStashStackEntry
{
    public required string DefinitionId { get; init; }

    public required int Quantity { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record SaveStashItem
{
    public required SaveItemInstance Item { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record SaveStackEntry
{
    public required string DefinitionId { get; init; }

    public required int Quantity { get; init; }
}

public sealed record SaveSlottedStackEntry
{
    public required int SlotIndex { get; init; }

    public required string DefinitionId { get; init; }

    public required int Quantity { get; init; }
}

public sealed record SaveItemInstance
{
    public required string InstanceId { get; init; }

    public required string DefinitionId { get; init; }

    public required int ItemLevel { get; init; }

    public required string Rarity { get; init; }

    public required int Temper { get; init; }

    public required IReadOnlyList<SaveAffix> Affixes { get; init; }

    public required bool Locked { get; init; }
}

public sealed record SaveAffix
{
    public required string AffixId { get; init; }

    public required double Value { get; init; }
}

public sealed record SaveSlottedItem
{
    public required int SlotIndex { get; init; }

    public required SaveItemInstance Item { get; init; }
}

public sealed record SaveEquippedItem
{
    public required string Slot { get; init; }

    public required SaveItemInstance Item { get; init; }
}

public sealed record SaveCurrency
{
    public required long Talers { get; init; }
}

public sealed record SaveQuestState
{
    public required string QuestId { get; init; }

    public required string Status { get; init; }

    public required IReadOnlyDictionary<string, int> Counters { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record SaveExploration
{
    public required double TotalMetersCredited { get; init; }

    public required SaveExplorationDaily? Daily { get; init; }

    public required IReadOnlyList<string> DiscoveredCellIds { get; init; }
}

public sealed record SaveExplorationDaily
{
    public required string DateUtc { get; init; }

    public required double MetersCredited { get; init; }

    public required int CellsDiscovered { get; init; }
}

public sealed record SaveEncounterResult
{
    public required string EncounterId { get; init; }

    public required string Source { get; init; }

    public required string Status { get; init; }

    public required string? WorldEntityId { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public required SaveRewardGrant Reward { get; init; }
}

public sealed record SaveRewardGrant
{
    public required long Exp { get; init; }

    public required long Talers { get; init; }

    public required IReadOnlyList<SaveStackEntry> Stacks { get; init; }

    public required IReadOnlyList<SaveGrantedItem> Items { get; init; }

    public required IReadOnlyList<SaveStackEntry> Materials { get; init; }
}

public sealed record SaveGrantedItem
{
    public required string DefinitionId { get; init; }

    public required int ItemLevel { get; init; }

    public required string Rarity { get; init; }

    public required string? InstanceId { get; init; }
}
