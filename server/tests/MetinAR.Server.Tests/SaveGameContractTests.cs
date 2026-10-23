using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MetinAR.Server.Api;
using MetinAR.Server.SaveGames;
using Xunit;
using static MetinAR.Server.Tests.SaveGameTestHelpers;

namespace MetinAR.Server.Tests;

/// <summary>Keeps docs/contracts (schema + examples) and the server DTOs/validator in step.</summary>
public class SaveGameContractTests
{
    private static readonly ExpCurveTable Curve = ExpCurveTable.LoadEmbedded();
    private static readonly ItemCatalog Catalog = ItemCatalog.LoadEmbedded();
    private static readonly CharacterRules Character = CharacterRules.LoadEmbedded(Catalog);

    private static JsonObject Schema() =>
        JsonNode.Parse(File.ReadAllText(RepoPath("docs", "contracts", "savegame.v1.schema.json")))!.AsObject();

    [Theory]
    [InlineData("savegame.v1.server.example.json", "server")]
    [InlineData("savegame.v1.local.example.json", "local")]
    public void DocsExamples_DeserializeStrictly_PassValidation_AndRoundTrip(string file, string mode)
    {
        string text = File.ReadAllText(RepoPath("docs", "contracts", "examples", file));

        SaveGameDocument document = SaveGameJson.Deserialize<SaveGameDocument>(text);
        var errors = new ValidationErrors();
        SaveGameValidator.Validate(document, Curve, Catalog, errors);

        Assert.False(errors.HasErrors, string.Join("; ", errors.ToDictionary().Select(e => e.Key + ": " + string.Join(",", e.Value))));
        Assert.Equal(mode, document.Mode);
        Assert.Equal(document.Character, Character.ClampVitals(document).Character);
        int levelUps = document.Character.Level - 1;
        SavePrimaryStats a = document.Stats.Allocated;
        Assert.Equal(levelUps * CharacterRules.StatPointsPerLevel, a.Might + a.Finesse + a.Spirit + a.Vigor + document.Stats.FreeStatPoints);
        Assert.Equal(levelUps * CharacterRules.SkillPointsPerLevel, document.Skills.FreeSkillPoints + document.Skills.Progress.Sum(p => p.PointsSpent));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(SaveGameJson.Serialize(document))),
            "The DTOs drop or rename a field present in the docs example.");
    }

    [Fact]
    public void LocalFileExample_HeaderChecksumAndLengthMatchTheBody()
    {
        byte[] bytes = File.ReadAllBytes(RepoPath("docs", "contracts", "examples", "savegame.v1.local.example.msave"));
        int newline = Array.IndexOf(bytes, (byte)'\n');
        string header = Encoding.ASCII.GetString(bytes, 0, newline);
        byte[] body = bytes[(newline + 1)..];

        string[] parts = header.Split(' ');
        Assert.Equal("METINAR-SAVE", parts[0]);
        Assert.Equal("1", parts[1]);
        Assert.Equal("sha256=" + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), parts[2]);
        Assert.Equal("bytes=" + body.Length, parts[3]);
        Assert.Equal(
            File.ReadAllText(RepoPath("docs", "contracts", "examples", "savegame.v1.local.example.json")),
            Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void Schema_TopLevelFieldsAndVersion_MatchTheServerDto()
    {
        JsonObject schema = Schema();
        string[] schemaFields = schema["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();
        string[] required = schema["required"]!.AsArray().Select(n => (string)n!).Order(StringComparer.Ordinal).ToArray();
        string[] dtoFields = typeof(SaveGameDocument).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(dtoFields, schemaFields);
        Assert.Equal(dtoFields, required);
        Assert.Equal(SaveGameMigrator.LatestVersion, (int)schema["properties"]!["schemaVersion"]!["const"]!);
        Assert.False((bool)schema["additionalProperties"]!);
    }

    [Fact]
    public void Schema_Enums_MatchTheServerRules()
    {
        string json = Schema().ToJsonString();
        foreach (string value in SaveGameRules.ClassIds.Concat(SaveGameRules.Rarities).Concat(SaveGameRules.EquipmentSlots).Concat(SaveGameRules.SkillTierIds))
        {
            Assert.Contains("\"" + value + "\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Schema_InventoryCapacityAndLootStash_MatchTheServerRules()
    {
        JsonObject inventory = Schema()["properties"]!["inventory"]!["properties"]!.AsObject();
        int[] capacities = inventory["capacity"]!["enum"]!.AsArray().Select(n => (int)n!).ToArray();
        int[] expected = Enumerable.Range(0, 100).Select(i => SaveGameRules.InventoryStartCapacity + (i * SaveGameRules.InventoryCapacityStep))
            .TakeWhile(c => c <= SaveGameRules.InventoryMaxCapacity).ToArray();
        Assert.Equal(expected, capacities);
        Assert.Equal([24, 32, 40, 48, 56], capacities);
        Assert.All(capacities, c => Assert.True(SaveGameRules.IsValidCapacity(c)));
        Assert.False(SaveGameRules.IsValidCapacity(25));

        JsonObject stash = inventory["lootStash"]!["properties"]!.AsObject();
        Assert.Equal(SaveGameRules.LootStashSlots, (int)stash["stacks"]!["maxItems"]!);
        Assert.Equal(SaveGameRules.LootStashSlots, (int)stash["items"]!["maxItems"]!);
        Assert.Equal(TimeSpan.FromHours(24), SaveGameRules.LootStashLifetime);
        Assert.Equal(LootStashAfterExpiry.Keep, new LootStashOptions().AfterExpiry); // §11.7: OPEN, default never lost
    }

    /// <summary>CharacterRules.Classes is a hand copy of CHARACTER_PROGRESSION §1.2; fail when the markdown changes.</summary>
    [Fact]
    public void ClassTable_MatchesCharacterProgressionMarkdown()
    {
        string[] lines = File.ReadAllLines(RepoPath("docs", "design", "CHARACTER_PROGRESSION.md"));
        var names = new Dictionary<string, string>
        {
            ["Moc"] = CharacterRules.Might,
            ["Zwinność"] = CharacterRules.Finesse,
            ["Duch"] = CharacterRules.Spirit,
            ["Krzepa"] = CharacterRules.Vigor,
        };
        int found = 0;
        foreach (ClassDefinition c in CharacterRules.Classes)
        {
            string row = Assert.Single(lines, l => l.StartsWith('|') && l.Contains("(`" + c.Id + "`)", StringComparison.Ordinal));
            string[] cells = row.Split('|', StringSplitOptions.TrimEntries)[1..^1];
            Assert.Equal(8, cells.Length);
            Assert.Equal(c.Start.Might, int.Parse(cells[1], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(c.Start.Finesse, int.Parse(cells[2], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(c.Start.Spirit, int.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(c.Start.Vigor, int.Parse(cells[4], System.Globalization.CultureInfo.InvariantCulture));
            string[] auto = cells[5].Split(',', StringSplitOptions.TrimEntries).Select(n => names[n]).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { c.AutoStatA, c.AutoStatB }.Order(StringComparer.Ordinal), auto);
            Assert.Equal(c.HpPerLevel, int.Parse(cells[6], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(c.MpPerLevel, int.Parse(cells[7], System.Globalization.CultureInfo.InvariantCulture));
            found++;
        }

        Assert.Equal(SaveGameRules.ClassIds.Count, found);
        Assert.Contains(lines, l => l.Contains("**2 wolne punkty**", StringComparison.Ordinal));
        // PROPOZYCJA_DOC: the default VitalsFormula coefficients are the doc's "propozycja" values (L37–L41).
        VitalsFormula f = new();
        Assert.Contains(lines, l => l.Contains($"`{f.BaseHp} + L × hpPerLevel(klasa) + Vigor × {f.HpPerVigor} + przedmioty`", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains($"`{f.BaseMp} + L × mpPerLevel(klasa) + Spirit × {f.MpPerSpirit} + przedmioty`", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Formuła (propozycja)", StringComparison.Ordinal));
        Assert.StartsWith("PROPOZYCJA_DOC", VitalsFormula.PropozycjaDoc, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => CharacterRules.LoadEmbedded(Catalog, new VitalsFormula { BaseHp = 0 }));
    }

    [Fact]
    public void ItemCatalog_LoadsTheDesignTables_WithSpecStackLimits()
    {
        Assert.Equal(50, Catalog.Find("potion_hp_s")!.StackLimit);
        Assert.Equal(99, Catalog.Find("ore_shard")!.StackLimit);
        Assert.Equal(ItemCatalog.CategoryMaterial, Catalog.Find("ore_shard")!.Category);
        Assert.Equal(1, Catalog.Find("starter_sword")!.StackLimit);
        Assert.Null(Catalog.Find("no_such_item"));
        Assert.All(SaveGameRules.ClassIds, c => Assert.Equal(20, Catalog.Find("treatise_proficiency_" + c)!.StackLimit));
        // K10 (shop table "consumable" vs skill_tier_items.csv): no save rule may depend on which version wins.
        Assert.All(SaveGameRules.ClassIds, c => Assert.DoesNotContain(
            Catalog.Find("treatise_proficiency_" + c)!.Category, new[] { ItemCatalog.CategoryEquipment, ItemCatalog.CategoryMaterial }));

        const string ItemsHeader = "itemId,namePl,category,slot,itemType,itemLevel,rarity,stack,baseStats,requiredClass,source,asset\n";
        const string ShopHeader = "itemId,namePl,category,itemLevel,rarity,stack,buyPrice,sellToNpcPrice,effect\n";
        const string TierHeader = "itemId,namePl,tierId,stack,sources,normalMobDropPct,eliteDropPct,guardianDropPct,minMobLevel,shopPrice,notes\n";
        Assert.ThrowsAny<ArgumentException>(() => ItemCatalog.Parse(
            ItemsHeader + "bad_item,X,material,,material,1,common,0,,any,drop,icon\n", ShopHeader, TierHeader, SaveGameRules.ClassIds));
        Assert.Throws<FormatException>(() => ItemCatalog.Parse(
            ItemsHeader + "dup_item,X,material,,material,1,common,99,,any,drop,icon\n", ShopHeader + "dup_item,X,material,1,common,20,1,1,x\n", TierHeader, SaveGameRules.ClassIds));
    }

    [Fact]
    public void Migrator_RunsInjectedStepsInOrder_AndRefusesNewerOrUnreachableVersions()
    {
        var migrator = new SaveGameMigrator(
            new Dictionary<int, Func<JsonObject, JsonObject>>
            {
                [1] = d => { d["stepOne"] = true; return d; },
                [2] = d => { d["afterOne"] = d["stepOne"] is not null; return d; },
            },
            currentVersion: 3,
            oldestReadableVersion: 1);

        Assert.Equal(SaveGameVersionCheck.Migrated, migrator.Upgrade(new JsonObject { ["schemaVersion"] = 1 }, 1, out JsonObject upgraded));
        Assert.Equal(3, (int)upgraded["schemaVersion"]!);
        Assert.True((bool)upgraded["afterOne"]!);
        Assert.Equal(SaveGameVersionCheck.Current, migrator.Upgrade(new JsonObject { ["schemaVersion"] = 3 }, 3, out _));
        Assert.Equal(SaveGameVersionCheck.TooNew, migrator.Upgrade(new JsonObject(), 4, out _));
        Assert.Equal(SaveGameVersionCheck.TooOld, migrator.Upgrade(new JsonObject(), 0, out _));

        var gap = new SaveGameMigrator(new Dictionary<int, Func<JsonObject, JsonObject>>(), currentVersion: 2, oldestReadableVersion: 1);
        Assert.Equal(SaveGameVersionCheck.TooOld, gap.Upgrade(new JsonObject(), 1, out _));
        Assert.Equal(SaveGameVersionCheck.Current, new SaveGameMigrator().Upgrade(new JsonObject(), 1, out _));
    }

    [Fact]
    public void ExpCurve_IsLoadedFromTheDesignTable()
    {
        Assert.Equal(99, Curve.MaxLevel);
        Assert.Equal(1, Curve.LevelForTotalExp(0));
        Assert.Equal(2, Curve.LevelForTotalExp(160));
        Assert.Equal(3, Curve.LevelForTotalExp(849));
        Assert.Equal(4, Curve.LevelForTotalExp(850));
        Assert.Equal(Curve.MaxLevel, Curve.LevelForTotalExp(long.MaxValue));
        Assert.Throws<FormatException>(() => ExpCurveTable.Parse("level,cumulativeExpAtLevelStart\n1,0\n3,10\n"));
    }
}
