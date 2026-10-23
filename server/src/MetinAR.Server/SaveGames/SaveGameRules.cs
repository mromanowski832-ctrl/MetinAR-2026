using System.Security.Cryptography;
using System.Text;
using MetinAR.Server.Domain;

namespace MetinAR.Server.SaveGames;

/// <summary>A client reward claim after validation (a proposal; the server decides what is granted).</summary>
public sealed record RewardClaim(
    long Exp,
    long Talers,
    IReadOnlyList<SaveStackEntry> Stacks,
    IReadOnlyList<RewardClaimItem> Items,
    IReadOnlyList<SaveStackEntry> Materials);

public sealed record RewardClaimItem(string DefinitionId, int ItemLevel, string Rarity);

/// <summary>Outcome of applying a reward to a document: the new document and what was actually granted, or why not.</summary>
public sealed record RewardApplication(SaveGameDocument? Document, SaveRewardGrant? Granted, string? RejectionReason);

/// <summary>Server-side SaveGame rules (contract §6–§8): initial save, server-owned fields, reward application.</summary>
public static class SaveGameRules
{
    public const string ModeLocal = "local";
    public const string ModeServer = "server";
    public const string SourceServer = "server";
    public const string SourceLocal = "local";
    public const string StatusApplied = "applied";
    public const string StatusPending = "pending";
    public const string StatusLocalUnverified = "local_unverified";
    public const string LocalEncounterIdPrefix = "local-";

    public const int SchemaMaxListItems = 200;
    public const int SchemaMaxEncounterResults = 200;
    public const int SchemaMaxRewardEntries = 10;
    public const int MaxStackQuantity = 999_999;

    public static IReadOnlyList<string> ClassIds { get; } = ["warrior", "ninja", "shaman", "slajter"];

    public static IReadOnlyList<string> SkillTierIds { get; } = ["practice", "proficiency", "artistry", "apex"];

    public static IReadOnlyList<string> Rarities { get; } = ["common", "uncommon", "rare", "epic", "legendary"];

    public static IReadOnlyList<string> EquipmentSlots { get; } =
        ["weapon", "off_hand", "helmet", "armor", "gloves", "boots", "earrings", "necklace", "bracelet", "ring", "quick_1", "quick_2"];

    /// <summary>
    /// The virtual revision-0 save of a player who has no stored SaveGame yet ("v0" = the pre-#71 player_profiles row):
    /// EXP and current HP/MP come from the legacy profile, everything else from <see cref="PrototypeSaveGameDefaults"/>.
    /// Deterministic, so repeated GETs return the same document (including the starter item id).
    /// </summary>
    public static SaveGameDocument CreateInitial(PlayerProfile profile, PrototypeSaveGameDefaults defaults, ExpCurveTable curve)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(curve);
        long totalExp = Math.Clamp(profile.Experience, 0, curve.TotalExpToMaxLevel);
        var equipment = new List<SaveEquippedItem>();
        if (!string.IsNullOrEmpty(defaults.StarterWeaponDefinitionId))
        {
            equipment.Add(new SaveEquippedItem
            {
                Slot = "weapon",
                Item = new SaveItemInstance
                {
                    InstanceId = DeterministicUuid(profile.Id + "/starter/" + defaults.StarterWeaponDefinitionId),
                    DefinitionId = defaults.StarterWeaponDefinitionId,
                    ItemLevel = 1,
                    Rarity = "common",
                    Temper = 0,
                    Affixes = [],
                    Locked = false,
                },
            });
        }

        return new SaveGameDocument
        {
            SchemaVersion = SaveGameMigrator.LatestVersion,
            Mode = ModeServer,
            PlayerId = profile.Id,
            Revision = 0,
            UpdatedAtUtc = profile.UpdatedAt,
            Profile = new SaveProfile { DisplayName = profile.DisplayName, CreatedAtUtc = profile.CreatedAt },
            Character = new SaveCharacter
            {
                ClassId = defaults.ClassId,
                PathId = null,
                Level = curve.LevelForTotalExp(totalExp),
                TotalExp = totalExp,
                HpCurrent = Math.Max(0, profile.CurrentHp),
                MpCurrent = Math.Max(0, profile.CurrentMp),
            },
            Stats = new SaveStats { Allocated = new SavePrimaryStats { Might = 0, Finesse = 0, Spirit = 0, Vigor = 0 }, FreeStatPoints = 0 },
            Skills = new SaveSkills
            {
                FreeSkillPoints = 0,
                Progress = [],
                Loadout = new SaveSkillLoadout { Attack = null, Aura = null, Slot1 = null, Slot2 = null },
            },
            Inventory = new SaveInventory
            {
                Capacity = defaults.InventoryCapacity,
                Stacks = [],
                Items = [],
                LootStash = new SaveLootStash { Stacks = [], Items = [] },
            },
            Equipment = equipment,
            Currency = new SaveCurrency { Talers = 0 },
            Materials = [],
            Quests = [],
            Exploration = new SaveExploration { TotalMetersCredited = 0, Daily = null, DiscoveredCellIds = [] },
            EncounterResults = [],
        };
    }

    /// <summary>
    /// Fields a client sync (PUT) must leave unchanged (contract §7). Progression, currency, item contents and
    /// history change only through server rules (rewards); the client may rearrange, equip, lock and rename.
    /// </summary>
    public static IReadOnlyList<string> ServerOwnedChanges(SaveGameDocument current, SaveGameDocument proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);
        var changed = new List<string>();
        void Check(string field, object a, object b)
        {
            if (!string.Equals(SaveGameJson.Serialize(a), SaveGameJson.Serialize(b), StringComparison.Ordinal))
            {
                changed.Add(field);
            }
        }

        Check("profile.createdAtUtc", current.Profile.CreatedAtUtc, proposed.Profile.CreatedAtUtc);
        Check("character.classId", current.Character.ClassId, proposed.Character.ClassId);
        Check("character.pathId", current.Character.PathId ?? string.Empty, proposed.Character.PathId ?? string.Empty);
        Check("character.totalExp", current.Character.TotalExp, proposed.Character.TotalExp);
        Check("character.level", current.Character.Level, proposed.Character.Level);
        Check("stats", current.Stats, proposed.Stats);
        Check("skills.freeSkillPoints", current.Skills.FreeSkillPoints, proposed.Skills.FreeSkillPoints);
        Check("skills.progress", current.Skills.Progress, proposed.Skills.Progress);
        Check("inventory.capacity", current.Inventory.Capacity, proposed.Inventory.Capacity);
        Check("items", ItemContents(current), ItemContents(proposed));
        Check("stackTotals", StackTotals(current), StackTotals(proposed));
        Check("currency", current.Currency, proposed.Currency);
        Check("materials", current.Materials, proposed.Materials);
        Check("quests", current.Quests, proposed.Quests);
        Check("exploration", current.Exploration, proposed.Exploration);
        Check("encounterResults", current.EncounterResults, proposed.EncounterResults);
        return changed;
    }

    /// <summary>
    /// Apply a validated claim (contract §8). EXP is capped at the max level, level is re-derived; stacks merge into
    /// an existing inventory stack or take the lowest free slot, items get a server-minted instance id; whatever does
    /// not fit goes to the loot stash. The result is appended to <c>encounterResults</c> (trimmed to the newest N).
    /// Level-up stat/skill point grants are NOT applied (open question S6).
    /// </summary>
    public static RewardApplication ApplyReward(
        SaveGameDocument current,
        RewardClaim claim,
        EncounterSession encounter,
        long newRevision,
        DateTimeOffset now,
        ExpCurveTable curve,
        int maxEncounterResults,
        Func<Guid> newInstanceId)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(newInstanceId);

        long totalExp = Math.Min(curve.TotalExpToMaxLevel, current.Character.TotalExp + claim.Exp);
        long grantedExp = totalExp - current.Character.TotalExp;
        long talers = checked(current.Currency.Talers + claim.Talers);

        var stacks = current.Inventory.Stacks.ToList();
        var items = current.Inventory.Items.ToList();
        var stashStacks = current.Inventory.LootStash.Stacks.ToList();
        var stashItems = current.Inventory.LootStash.Items.ToList();
        var used = new HashSet<int>(stacks.Select(s => s.SlotIndex).Concat(items.Select(i => i.SlotIndex)));
        int? FreeSlot()
        {
            for (int slot = 0; slot < current.Inventory.Capacity; slot++)
            {
                if (used.Add(slot))
                {
                    return slot;
                }
            }

            return null;
        }

        foreach (SaveStackEntry add in claim.Stacks)
        {
            int index = stacks.FindIndex(s => s.DefinitionId == add.DefinitionId && s.Quantity + add.Quantity <= MaxStackQuantity);
            if (index >= 0)
            {
                stacks[index] = stacks[index] with { Quantity = stacks[index].Quantity + add.Quantity };
            }
            else if (FreeSlot() is int slot)
            {
                stacks.Add(new SaveSlottedStackEntry { SlotIndex = slot, DefinitionId = add.DefinitionId, Quantity = add.Quantity });
            }
            else if (!MergeInto(stashStacks, add, uniqueDefinition: false))
            {
                return new RewardApplication(null, null, "The loot stash is full.");
            }
        }

        var grantedItems = new List<SaveGrantedItem>();
        foreach (RewardClaimItem add in claim.Items)
        {
            var instance = new SaveItemInstance
            {
                InstanceId = newInstanceId().ToString("D"),
                DefinitionId = add.DefinitionId,
                ItemLevel = add.ItemLevel,
                Rarity = add.Rarity,
                Temper = 0,
                Affixes = [],
                Locked = false,
            };
            if (FreeSlot() is int slot)
            {
                items.Add(new SaveSlottedItem { SlotIndex = slot, Item = instance });
            }
            else if (stashItems.Count < SchemaMaxListItems)
            {
                stashItems.Add(instance);
            }
            else
            {
                return new RewardApplication(null, null, "The loot stash is full.");
            }

            grantedItems.Add(new SaveGrantedItem { DefinitionId = add.DefinitionId, ItemLevel = add.ItemLevel, Rarity = add.Rarity, InstanceId = instance.InstanceId });
        }

        var materials = current.Materials.ToList();
        foreach (SaveStackEntry add in claim.Materials)
        {
            if (!MergeInto(materials, add, uniqueDefinition: true))
            {
                return new RewardApplication(null, null, "The material bag is full.");
            }
        }

        var granted = new SaveRewardGrant
        {
            Exp = grantedExp,
            Talers = claim.Talers,
            Stacks = claim.Stacks,
            Items = grantedItems,
            Materials = claim.Materials,
        };
        var results = current.EncounterResults.ToList();
        results.Add(new SaveEncounterResult
        {
            EncounterId = encounter.Id,
            Source = SourceServer,
            Status = StatusApplied,
            WorldEntityId = encounter.WorldEntityId,
            CompletedAtUtc = encounter.CompletedAt ?? now,
            Reward = granted,
        });
        if (results.Count > maxEncounterResults)
        {
            results.RemoveRange(0, results.Count - maxEncounterResults);
        }

        SaveGameDocument next = current with
        {
            Mode = ModeServer,
            Revision = newRevision,
            UpdatedAtUtc = now,
            Character = current.Character with { TotalExp = totalExp, Level = curve.LevelForTotalExp(totalExp) },
            Currency = new SaveCurrency { Talers = talers },
            Inventory = current.Inventory with
            {
                Stacks = stacks,
                Items = items,
                LootStash = new SaveLootStash { Stacks = stashStacks, Items = stashItems },
            },
            Materials = materials,
            EncounterResults = results,
        };
        return new RewardApplication(next, granted, null);
    }

    /// <summary>UUID (RFC 4122 layout, version 8 = custom) derived from SHA-256 of the seed; lower-case.</summary>
    public static string DeterministicUuid(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        string hex = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }

    private static bool MergeInto(List<SaveStackEntry> list, SaveStackEntry add, bool uniqueDefinition)
    {
        int index = list.FindIndex(s => s.DefinitionId == add.DefinitionId);
        if (index >= 0 && list[index].Quantity + add.Quantity <= MaxStackQuantity)
        {
            list[index] = list[index] with { Quantity = list[index].Quantity + add.Quantity };
            return true;
        }

        if (list.Count >= SchemaMaxListItems || (uniqueDefinition && index >= 0))
        {
            return false;
        }

        list.Add(new SaveStackEntry { DefinitionId = add.DefinitionId, Quantity = add.Quantity });
        return true;
    }

    private static SortedDictionary<string, SaveItemInstance> ItemContents(SaveGameDocument document)
    {
        var all = new SortedDictionary<string, SaveItemInstance>(StringComparer.Ordinal);
        IEnumerable<SaveItemInstance> instances = document.Inventory.Items.Select(i => i.Item)
            .Concat(document.Inventory.LootStash.Items)
            .Concat(document.Equipment.Select(e => e.Item));
        foreach (SaveItemInstance item in instances)
        {
            all[item.InstanceId] = item with { Locked = false };
        }

        return all;
    }

    private static SortedDictionary<string, long> StackTotals(SaveGameDocument document)
    {
        var totals = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach ((string id, int quantity) in document.Inventory.Stacks.Select(s => (s.DefinitionId, s.Quantity))
            .Concat(document.Inventory.LootStash.Stacks.Select(s => (s.DefinitionId, s.Quantity))))
        {
            totals[id] = totals.GetValueOrDefault(id) + quantity;
        }

        return totals;
    }
}
