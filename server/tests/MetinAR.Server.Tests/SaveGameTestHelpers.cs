using System.Text;
using System.Text.Json.Nodes;

namespace MetinAR.Server.Tests;

/// <summary>Helpers for the SaveGame contract (#71).</summary>
public static class SaveGameTestHelpers
{
    public static string SaveUrl(string playerId) => $"/api/v1/player/{playerId}/savegame";

    /// <summary>GET the save; returns the HTTP ETag and the <c>saveGame</c> object.</summary>
    public static async Task<(string ETag, JsonObject SaveGame)> GetSaveAsync(this HttpClient client, string playerId)
    {
        HttpResponseMessage r = await client.GetAsync(SaveUrl(playerId));
        string text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET savegame failed {(int)r.StatusCode}: {text}");
        }

        JsonObject body = JsonNode.Parse(text)!.AsObject();
        return (r.Headers.ETag!.Tag, body["saveGame"]!.AsObject());
    }

    public static Task<HttpResponseMessage> PutSaveAsync(this HttpClient client, string playerId, JsonNode body, string? ifMatch) =>
        client.PutSaveRawAsync(playerId, body.ToJsonString(), ifMatch);

    public static Task<HttpResponseMessage> PutSaveRawAsync(this HttpClient client, string playerId, string json, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, SaveUrl(playerId))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    public static object Claim(long exp = 0, long talers = 0, object[]? stacks = null, object[]? items = null, object[]? materials = null) =>
        new { exp, talers, stacks = stacks ?? [], items = items ?? [], materials = materials ?? [] };

    public static Task<HttpResponseMessage> RewardAsync(this HttpClient client, string encounterId, string playerId, string nonce, object claim) =>
        client.PostJsonAsync("/api/v1/encounters/reward", new { encounterId, playerId, nonce, claim });

    /// <summary>Start and complete an encounter at the entity; returns (encounterId, nonce).</summary>
    public static async Task<(string Id, string Nonce)> CompletedEncounterAsync(this HttpClient client, string playerId, string entityId)
    {
        (string id, string nonce) = await client.StartAtAsync(playerId, entityId);
        HttpResponseMessage done = await client.CompleteAsync(id, playerId, nonce);
        done.EnsureSuccessStatusCode();
        return (id, nonce);
    }

    public static async Task<string> CodeAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("code").GetString()!;

    public static string RepoPath(params string[] parts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "contracts")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Repository root with docs/contracts not found.");
        }

        return Path.Combine([dir.FullName, .. parts]);
    }
}
