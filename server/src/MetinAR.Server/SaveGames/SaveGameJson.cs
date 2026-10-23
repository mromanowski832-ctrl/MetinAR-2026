using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MetinAR.Server.SaveGames;

/// <summary>Canonical SaveGame serialization (same rules as the HTTP API) and its SHA-256 checksum.</summary>
public static class SaveGameJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("JSON value is null.");

    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes.</summary>
    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        ServerSetup.ConfigureJson(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
