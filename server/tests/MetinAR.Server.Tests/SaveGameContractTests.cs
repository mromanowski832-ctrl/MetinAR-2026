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
        SaveGameValidator.Validate(document, Curve, errors);

        Assert.False(errors.HasErrors, string.Join("; ", errors.ToDictionary().Select(e => e.Key + ": " + string.Join(",", e.Value))));
        Assert.Equal(mode, document.Mode);
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
