using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using MetinAR.Server.Api;

namespace MetinAR.Server.SaveGames;

/// <summary>
/// Structural + semantic validation of a SaveGame v1 document, equivalent to docs/contracts/savegame.v1.schema.json
/// plus the cross-field rules the schema cannot express (derived level, unique ids/slots, slot range). Implemented in
/// code on purpose: no JSON Schema runtime dependency (see the contract doc, "Walidacja").
/// System.Text.Json enforces `required` members but not nullability, so every reference is null-checked here.
/// </summary>
public static partial class SaveGameValidator
{
    public const int MaxDisplayNameLength = 32;

    [GeneratedRegex("^[A-Za-z0-9_-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterKeyPattern();

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();

    public static bool IsId(string? value) => value is not null && IdPattern().IsMatch(value);

    public static bool IsUuid(string? value) => value is not null && UuidPattern().IsMatch(value);

    public static void Validate(SaveGameDocument document, ExpCurveTable curve, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(errors);

        if (document.SchemaVersion != SaveGameMigrator.LatestVersion)
        {
            errors.Add("schemaVersion", $"Must be {SaveGameMigrator.LatestVersion}.");
        }

        if (document.Mode is not (SaveGameRules.ModeLocal or SaveGameRules.ModeServer))
        {
            errors.Add("mode", "Must be 'local' or 'server'.");
        }

        Id(errors, "playerId", document.PlayerId);
        if (document.Revision < 0)
        {
            errors.Add("revision", "Must be >= 0.");
        }
        else if (document.Mode == SaveGameRules.ModeLocal && document.Revision != 0)
        {
            errors.Add("revision", "Must be 0 in local mode (local saves are never server-confirmed).");
        }

        if (Required(errors, "profile", document.Profile))
        {
            DisplayName(errors, "profile.displayName", document.Profile.DisplayName);
        }

        if (Required(errors, "character", document.Character))
        {
            Character(errors, document.Character, curve);
        }

        if (Required(errors, "stats", document.Stats))
        {
            if (Required(errors, "stats.allocated", document.Stats.Allocated))
            {
                NonNegative(errors, "stats.allocated.might", document.Stats.Allocated.Might);
                NonNegative(errors, "stats.allocated.finesse", document.Stats.Allocated.Finesse);
                NonNegative(errors, "stats.allocated.spirit", document.Stats.Allocated.Spirit);
                NonNegative(errors, "stats.allocated.vigor", document.Stats.Allocated.Vigor);
            }

            NonNegative(errors, "stats.freeStatPoints", document.Stats.FreeStatPoints);
        }

        if (Required(errors, "skills", document.Skills))
        {
            Skills(errors, document.Skills);
        }

        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        if (Required(errors, "inventory", document.Inventory))
        {
            Inventory(errors, document.Inventory, instanceIds);
        }

        if (Required(errors, "equipment", document.Equipment))
        {
            Equipment(errors, document.Equipment, instanceIds);
        }

        if (Required(errors, "currency", document.Currency) && document.Currency.Talers < 0)
        {
            errors.Add("currency.talers", "Must be >= 0.");
        }

        if (Required(errors, "materials", document.Materials))
        {
            Stacks(errors, "materials", document.Materials, SaveGameRules.SchemaMaxListItems, requireUniqueDefinition: true);
        }

        if (Required(errors, "quests", document.Quests))
        {
            Quests(errors, document.Quests);
        }

        if (Required(errors, "exploration", document.Exploration))
        {
            Exploration(errors, document.Exploration);
        }

        if (Required(errors, "encounterResults", document.EncounterResults))
        {
            EncounterResults(errors, document.EncounterResults, document.Mode);
        }
    }

    public static void DisplayName(ValidationErrors errors, string field, string? value)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Required.");
        }
        else if (value.Length > MaxDisplayNameLength)
        {
            errors.Add(field, $"Must be at most {MaxDisplayNameLength} characters.");
        }
        else if (value.Any(char.IsControl))
        {
            errors.Add(field, "Must not contain control characters.");
        }
    }

    private static void Character(ValidationErrors errors, SaveCharacter character, ExpCurveTable curve)
    {
        if (character.ClassId is null || !SaveGameRules.ClassIds.Contains(character.ClassId))
        {
            errors.Add("character.classId", "Must be one of: " + string.Join(", ", SaveGameRules.ClassIds) + ".");
        }

        OptionalId(errors, "character.pathId", character.PathId);
        if (character.TotalExp < 0 || character.TotalExp > curve.TotalExpToMaxLevel)
        {
            errors.Add("character.totalExp", $"Must be 0-{curve.TotalExpToMaxLevel.ToString(CultureInfo.InvariantCulture)}.");
        }
        else if (character.Level != curve.LevelForTotalExp(character.TotalExp))
        {
            errors.Add("character.level", "Must equal the level derived from totalExp (exp_curve.csv).");
        }

        if (character.Level < 1 || character.Level > curve.MaxLevel)
        {
            errors.Add("character.level", $"Must be 1-{curve.MaxLevel}.");
        }

        NonNegative(errors, "character.hpCurrent", character.HpCurrent);
        NonNegative(errors, "character.mpCurrent", character.MpCurrent);
    }

    private static void Skills(ValidationErrors errors, SaveSkills skills)
    {
        NonNegative(errors, "skills.freeSkillPoints", skills.FreeSkillPoints);
        if (Required(errors, "skills.progress", skills.Progress))
        {
            MaxItems(errors, "skills.progress", skills.Progress.Count, 64);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < skills.Progress.Count; i++)
            {
                string path = $"skills.progress[{i}]";
                SaveSkillProgress? p = skills.Progress[i];
                if (!Required(errors, path, p))
                {
                    continue;
                }

                Id(errors, path + ".skillId", p.SkillId);
                if (p.SkillId is not null && !seen.Add(p.SkillId))
                {
                    errors.Add(path + ".skillId", "Duplicate skillId.");
                }

                Range(errors, path + ".rankIndex", p.RankIndex, 1, 29);
                if (p.TierId is null || !SaveGameRules.SkillTierIds.Contains(p.TierId))
                {
                    errors.Add(path + ".tierId", "Must be one of: " + string.Join(", ", SaveGameRules.SkillTierIds) + ".");
                }

                Range(errors, path + ".tierRank", p.TierRank, 1, 14);
                NonNegative(errors, path + ".pointsSpent", p.PointsSpent);
                NonNegative(errors, path + ".attemptsTotal", p.AttemptsTotal);
            }
        }

        if (Required(errors, "skills.loadout", skills.Loadout))
        {
            OptionalId(errors, "skills.loadout.attack", skills.Loadout.Attack);
            OptionalId(errors, "skills.loadout.aura", skills.Loadout.Aura);
            OptionalId(errors, "skills.loadout.slot1", skills.Loadout.Slot1);
            OptionalId(errors, "skills.loadout.slot2", skills.Loadout.Slot2);
        }
    }

    private static void Inventory(ValidationErrors errors, SaveInventory inventory, HashSet<string> instanceIds)
    {
        bool capacityOk = inventory.Capacity is >= 1 and <= SaveGameRules.SchemaMaxListItems;
        if (!capacityOk)
        {
            errors.Add("inventory.capacity", $"Must be 1-{SaveGameRules.SchemaMaxListItems}.");
        }

        var slots = new HashSet<int>();
        void Slot(string path, int slotIndex)
        {
            if (slotIndex < 0 || (capacityOk && slotIndex >= inventory.Capacity))
            {
                errors.Add(path, "Must be 0 to capacity-1.");
            }
            else if (!slots.Add(slotIndex))
            {
                errors.Add(path, "Slot already occupied.");
            }
        }

        if (Required(errors, "inventory.stacks", inventory.Stacks))
        {
            MaxItems(errors, "inventory.stacks", inventory.Stacks.Count, SaveGameRules.SchemaMaxListItems);
            for (int i = 0; i < inventory.Stacks.Count; i++)
            {
                string path = $"inventory.stacks[{i}]";
                SaveSlottedStackEntry? s = inventory.Stacks[i];
                if (Required(errors, path, s))
                {
                    Slot(path + ".slotIndex", s.SlotIndex);
                    Id(errors, path + ".definitionId", s.DefinitionId);
                    Quantity(errors, path + ".quantity", s.Quantity);
                }
            }
        }

        if (Required(errors, "inventory.items", inventory.Items))
        {
            MaxItems(errors, "inventory.items", inventory.Items.Count, SaveGameRules.SchemaMaxListItems);
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                string path = $"inventory.items[{i}]";
                SaveSlottedItem? s = inventory.Items[i];
                if (Required(errors, path, s))
                {
                    Slot(path + ".slotIndex", s.SlotIndex);
                    Item(errors, path + ".item", s.Item, instanceIds);
                }
            }
        }

        if (Required(errors, "inventory.lootStash", inventory.LootStash))
        {
            if (Required(errors, "inventory.lootStash.stacks", inventory.LootStash.Stacks))
            {
                Stacks(errors, "inventory.lootStash.stacks", inventory.LootStash.Stacks, SaveGameRules.SchemaMaxListItems, requireUniqueDefinition: false);
            }

            if (Required(errors, "inventory.lootStash.items", inventory.LootStash.Items))
            {
                MaxItems(errors, "inventory.lootStash.items", inventory.LootStash.Items.Count, SaveGameRules.SchemaMaxListItems);
                for (int i = 0; i < inventory.LootStash.Items.Count; i++)
                {
                    Item(errors, $"inventory.lootStash.items[{i}]", inventory.LootStash.Items[i], instanceIds);
                }
            }
        }
    }

    private static void Equipment(ValidationErrors errors, IReadOnlyList<SaveEquippedItem> equipment, HashSet<string> instanceIds)
    {
        MaxItems(errors, "equipment", equipment.Count, SaveGameRules.EquipmentSlots.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < equipment.Count; i++)
        {
            string path = $"equipment[{i}]";
            SaveEquippedItem? e = equipment[i];
            if (!Required(errors, path, e))
            {
                continue;
            }

            if (e.Slot is null || !SaveGameRules.EquipmentSlots.Contains(e.Slot))
            {
                errors.Add(path + ".slot", "Must be one of: " + string.Join(", ", SaveGameRules.EquipmentSlots) + ".");
            }
            else if (!seen.Add(e.Slot))
            {
                errors.Add(path + ".slot", "Duplicate equipment slot.");
            }

            Item(errors, path + ".item", e.Item, instanceIds);
        }
    }

    private static void Item(ValidationErrors errors, string path, SaveItemInstance? item, HashSet<string> instanceIds)
    {
        if (!Required(errors, path, item))
        {
            return;
        }

        if (!IsUuid(item.InstanceId))
        {
            errors.Add(path + ".instanceId", "Must be a lower-case UUID.");
        }
        else if (!instanceIds.Add(item.InstanceId))
        {
            errors.Add(path + ".instanceId", "Duplicate instanceId (an item exists exactly once across inventory, stash and equipment).");
        }

        Id(errors, path + ".definitionId", item.DefinitionId);
        Range(errors, path + ".itemLevel", item.ItemLevel, 1, 99);
        Rarity(errors, path + ".rarity", item.Rarity);
        Range(errors, path + ".temper", item.Temper, 0, 10);
        if (Required(errors, path + ".affixes", item.Affixes))
        {
            MaxItems(errors, path + ".affixes", item.Affixes.Count, 8);
            for (int i = 0; i < item.Affixes.Count; i++)
            {
                SaveAffix? a = item.Affixes[i];
                if (Required(errors, $"{path}.affixes[{i}]", a))
                {
                    Id(errors, $"{path}.affixes[{i}].affixId", a.AffixId);
                    if (!double.IsFinite(a.Value))
                    {
                        errors.Add($"{path}.affixes[{i}].value", "Must be a finite number.");
                    }
                }
            }
        }
    }

    private static void Quests(ValidationErrors errors, IReadOnlyList<SaveQuestState> quests)
    {
        MaxItems(errors, "quests", quests.Count, SaveGameRules.SchemaMaxListItems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < quests.Count; i++)
        {
            string path = $"quests[{i}]";
            SaveQuestState? q = quests[i];
            if (!Required(errors, path, q))
            {
                continue;
            }

            Id(errors, path + ".questId", q.QuestId);
            if (q.QuestId is not null && !seen.Add(q.QuestId))
            {
                errors.Add(path + ".questId", "Duplicate questId.");
            }

            if (q.Status is not ("active" or "completed"))
            {
                errors.Add(path + ".status", "Must be 'active' or 'completed'.");
            }

            if (Required(errors, path + ".counters", q.Counters))
            {
                if (q.Counters.Count > 16)
                {
                    errors.Add(path + ".counters", "At most 16 counters.");
                }

                foreach ((string key, int value) in q.Counters)
                {
                    if (!CounterKeyPattern().IsMatch(key))
                    {
                        errors.Add(path + ".counters", "Counter keys must be 1-64 characters of A-Z, a-z, 0-9, '_' or '-'.");
                    }

                    NonNegative(errors, $"{path}.counters.{key}", value);
                }
            }
        }
    }

    private static void Exploration(ValidationErrors errors, SaveExploration exploration)
    {
        NonNegativeFinite(errors, "exploration.totalMetersCredited", exploration.TotalMetersCredited);
        if (exploration.Daily is not null)
        {
            if (!DateOnly.TryParseExact(exploration.Daily.DateUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("exploration.daily.dateUtc", "Must be a date yyyy-MM-dd (UTC).");
            }

            NonNegativeFinite(errors, "exploration.daily.metersCredited", exploration.Daily.MetersCredited);
            NonNegative(errors, "exploration.daily.cellsDiscovered", exploration.Daily.CellsDiscovered);
        }

        if (Required(errors, "exploration.discoveredCellIds", exploration.DiscoveredCellIds))
        {
            MaxItems(errors, "exploration.discoveredCellIds", exploration.DiscoveredCellIds.Count, 10_000);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < exploration.DiscoveredCellIds.Count; i++)
            {
                string? id = exploration.DiscoveredCellIds[i];
                if (!IsId(id))
                {
                    errors.Add($"exploration.discoveredCellIds[{i}]", Validation.IdRule);
                }
                else if (!seen.Add(id!))
                {
                    errors.Add($"exploration.discoveredCellIds[{i}]", "Duplicate cell id.");
                }
            }
        }
    }

    private static void EncounterResults(ValidationErrors errors, IReadOnlyList<SaveEncounterResult> results, string mode)
    {
        MaxItems(errors, "encounterResults", results.Count, SaveGameRules.SchemaMaxEncounterResults);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < results.Count; i++)
        {
            string path = $"encounterResults[{i}]";
            SaveEncounterResult? r = results[i];
            if (!Required(errors, path, r))
            {
                continue;
            }

            Id(errors, path + ".encounterId", r.EncounterId);
            if (r.EncounterId is not null && !seen.Add(r.EncounterId))
            {
                errors.Add(path + ".encounterId", "Duplicate encounterId (a reward is applied at most once per encounter).");
            }

            bool localId = r.EncounterId?.StartsWith(SaveGameRules.LocalEncounterIdPrefix, StringComparison.Ordinal) == true;
            switch (r.Source)
            {
                case SaveGameRules.SourceLocal:
                    if (!localId)
                    {
                        errors.Add(path + ".encounterId", $"Local encounter ids must start with '{SaveGameRules.LocalEncounterIdPrefix}'.");
                    }

                    if (r.Status != SaveGameRules.StatusLocalUnverified)
                    {
                        errors.Add(path + ".status", "A local result must be 'local_unverified'.");
                    }

                    if (mode == SaveGameRules.ModeServer)
                    {
                        errors.Add(path + ".source", "A server-mode save cannot contain local results (import is not designed yet, #39).");
                    }

                    break;
                case SaveGameRules.SourceServer:
                    if (localId)
                    {
                        errors.Add(path + ".encounterId", $"The '{SaveGameRules.LocalEncounterIdPrefix}' prefix is reserved for local results.");
                    }

                    if (r.Status is not (SaveGameRules.StatusApplied or SaveGameRules.StatusPending))
                    {
                        errors.Add(path + ".status", "A server result must be 'applied' or 'pending'.");
                    }

                    break;
                default:
                    errors.Add(path + ".source", "Must be 'server' or 'local'.");
                    break;
            }

            OptionalId(errors, path + ".worldEntityId", r.WorldEntityId);
            if (Required(errors, path + ".reward", r.Reward))
            {
                Grant(errors, path + ".reward", r.Reward);
            }
        }
    }

    private static void Grant(ValidationErrors errors, string path, SaveRewardGrant grant)
    {
        if (grant.Exp < 0)
        {
            errors.Add(path + ".exp", "Must be >= 0.");
        }

        if (grant.Talers < 0)
        {
            errors.Add(path + ".talers", "Must be >= 0.");
        }

        if (Required(errors, path + ".stacks", grant.Stacks))
        {
            Stacks(errors, path + ".stacks", grant.Stacks, SaveGameRules.SchemaMaxRewardEntries, requireUniqueDefinition: false);
        }

        if (Required(errors, path + ".materials", grant.Materials))
        {
            Stacks(errors, path + ".materials", grant.Materials, SaveGameRules.SchemaMaxRewardEntries, requireUniqueDefinition: false);
        }

        if (Required(errors, path + ".items", grant.Items))
        {
            MaxItems(errors, path + ".items", grant.Items.Count, SaveGameRules.SchemaMaxRewardEntries);
            for (int i = 0; i < grant.Items.Count; i++)
            {
                string itemPath = $"{path}.items[{i}]";
                SaveGrantedItem? item = grant.Items[i];
                if (!Required(errors, itemPath, item))
                {
                    continue;
                }

                Id(errors, itemPath + ".definitionId", item.DefinitionId);
                Range(errors, itemPath + ".itemLevel", item.ItemLevel, 1, 99);
                Rarity(errors, itemPath + ".rarity", item.Rarity);
                if (item.InstanceId is not null && !IsUuid(item.InstanceId))
                {
                    errors.Add(itemPath + ".instanceId", "Must be null or a lower-case UUID.");
                }
            }
        }
    }

    internal static void Stacks(ValidationErrors errors, string path, IReadOnlyList<SaveStackEntry> stacks, int maxItems, bool requireUniqueDefinition)
    {
        MaxItems(errors, path, stacks.Count, maxItems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < stacks.Count; i++)
        {
            SaveStackEntry? s = stacks[i];
            if (!Required(errors, $"{path}[{i}]", s))
            {
                continue;
            }

            Id(errors, $"{path}[{i}].definitionId", s.DefinitionId);
            if (requireUniqueDefinition && s.DefinitionId is not null && !seen.Add(s.DefinitionId))
            {
                errors.Add($"{path}[{i}].definitionId", "Duplicate definitionId.");
            }

            Quantity(errors, $"{path}[{i}].quantity", s.Quantity);
        }
    }

    internal static void Rarity(ValidationErrors errors, string field, string? rarity)
    {
        if (rarity is null || !SaveGameRules.Rarities.Contains(rarity))
        {
            errors.Add(field, "Must be one of: " + string.Join(", ", SaveGameRules.Rarities) + ".");
        }
    }

    internal static void Id(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(field, "Required.");
        }
        else if (!IsId(value))
        {
            errors.Add(field, Validation.IdRule);
        }
    }

    private static void OptionalId(ValidationErrors errors, string field, string? value)
    {
        if (value is not null && !IsId(value))
        {
            errors.Add(field, "Must be null or " + char.ToLowerInvariant(Validation.IdRule[0]) + Validation.IdRule[1..]);
        }
    }

    private static bool Required<T>(ValidationErrors errors, string field, [NotNullWhen(true)] T? value)
        where T : class
    {
        if (value is null)
        {
            errors.Add(field, "Required.");
            return false;
        }

        return true;
    }

    private static void Quantity(ValidationErrors errors, string field, int value) => Range(errors, field, value, 1, SaveGameRules.MaxStackQuantity);

    private static void NonNegative(ValidationErrors errors, string field, int value)
    {
        if (value < 0)
        {
            errors.Add(field, "Must be >= 0.");
        }
    }

    private static void NonNegativeFinite(ValidationErrors errors, string field, double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            errors.Add(field, "Must be a finite number >= 0.");
        }
    }

    private static void Range(ValidationErrors errors, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add(field, $"Must be {min}-{max}.");
        }
    }

    private static void MaxItems(ValidationErrors errors, string field, int count, int max)
    {
        if (count > max)
        {
            errors.Add(field, $"At most {max} entries.");
        }
    }
}
