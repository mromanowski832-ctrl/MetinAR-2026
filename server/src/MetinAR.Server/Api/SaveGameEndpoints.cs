using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MetinAR.Server.Domain;
using MetinAR.Server.Persistence;
using MetinAR.Server.SaveGames;
using Microsoft.Extensions.Options;

namespace MetinAR.Server.Api;

// METINAR-035 / #71: persistent SaveGame + idempotent encounter rewards. Contract: docs/METINAR-071-SAVEGAME-CONTRACT.md.
// PROTOTYPE: no authentication (localhost only, like the rest of /api/v1) — player identity is #39.
public static partial class Endpoints
{
    private const string LogCategory = "MetinAR.Server.SaveGames";

    private static void MapSaveGameApi(RouteGroupBuilder v1)
    {
        v1.MapGet("/player/{playerId}/savegame", GetSaveGameAsync)
            .WithName("GetSaveGame").WithTags("SaveGame")
            .Produces<SaveGameResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        v1.MapPut("/player/{playerId}/savegame", PutSaveGameAsync)
            .WithName("PutSaveGame").WithTags("SaveGame")
            .Accepts<SaveGameDocument>("application/json")
            .Produces<SaveGameResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        v1.MapPost("/encounters/reward", ClaimEncounterRewardAsync)
            .WithName("ClaimEncounterReward").WithTags("Encounters")
            .Accepts<ClaimEncounterRewardRequest>("application/json")
            .Produces<ClaimEncounterRewardResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    [GeneratedRegex("^\"([0-9]{1,18})\"$", RegexOptions.CultureInvariant)]
    private static partial Regex StrongEtagPattern();

    internal static async Task<IResult> GetSaveGameAsync(
        HttpContext http, string playerId, IPlayerStore players, ISaveGameStore saves, ExpCurveTable curve, CharacterRules character,
        IOptions<PrototypeSaveGameDefaults> defaults, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!Validation.IsValidId(playerId))
        {
            var errors = new ValidationErrors();
            errors.Add("playerId", Validation.IdRule);
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        PlayerProfile? profile = await players.GetAsync(playerId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            return PlayerNotFound(http, playerId);
        }

        (StoredSaveGame? stored, IResult? failure) = await LoadAsync(http, saves, playerId, loggers, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        // Expired loot-stash entries are lost (#71 decision 7): filtered on read, dropped from storage on the next write.
        SaveGameDocument document = SaveGameRules.PruneExpiredStash(
            stored?.Document ?? SaveGameRules.CreateInitial(profile, defaults.Value, curve, character), UtcTimestamp.Normalize(time.GetUtcNow()));
        return SaveGameOk(http, document, time);
    }

    internal static async Task<IResult> PutSaveGameAsync(
        HttpContext http, string playerId, IPlayerStore players, ISaveGameStore saves, ExpCurveTable curve, CharacterRules character,
        SaveGameMigrator migrator, IOptions<PrototypeSaveGameDefaults> defaults, IOptions<SaveGameLimits> limits,
        TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Validation.Id(errors, "playerId", playerId);
        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        string? ifMatch = http.Request.Headers.IfMatch;
        if (string.IsNullOrEmpty(ifMatch))
        {
            return ApiErrors.Result(http, StatusCodes.Status428PreconditionRequired, ErrorCodes.SaveGamePreconditionRequired,
                "If-Match is required.", "Send If-Match: \"<revision>\" with the revision (ETag) you last read.");
        }

        Match etag = StrongEtagPattern().Match(ifMatch);
        if (!etag.Success)
        {
            errors.Add("If-Match", "Must be a single strong ETag \"<revision>\" as returned by GET.");
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        long expectedRevision = long.Parse(etag.Groups[1].Value, CultureInfo.InvariantCulture);

        (SaveGameDocument? incoming, IResult? readFailure) = await ReadDocumentAsync(http, migrator, limits.Value, ct).ConfigureAwait(false);
        if (readFailure is not null)
        {
            return readFailure;
        }

        incoming = incoming! with { Profile = incoming.Profile is null ? null! : incoming.Profile with { DisplayName = incoming.Profile.DisplayName?.Trim()! } };
        SaveGameValidator.Validate(incoming, curve, character.Catalog, errors);
        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        if (incoming.Mode == SaveGameRules.ModeLocal)
        {
            return ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.SaveGameLocalModeNotSyncable,
                "Local saves cannot be synced.",
                "A local-mode save is unverified. Importing local progress is an open owner decision (#39); start from GET.");
        }

        if (!string.Equals(incoming.PlayerId, playerId, StringComparison.Ordinal))
        {
            errors.Add("playerId", "Must equal the playerId in the URL.");
        }

        if (incoming.Revision != expectedRevision)
        {
            errors.Add("revision", "Must equal the If-Match revision.");
        }

        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        PlayerProfile? profile = await players.GetAsync(playerId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            return PlayerNotFound(http, playerId);
        }

        (StoredSaveGame? stored, IResult? failure) = await LoadAsync(http, saves, playerId, loggers, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        SaveGameDocument current = SaveGameRules.PruneExpiredStash(
            stored?.Document ?? SaveGameRules.CreateInitial(profile, defaults.Value, curve, character), now);
        if (current.Revision != expectedRevision)
        {
            return RevisionConflict(http, current.Revision);
        }

        // Both sides are pruned at the same instant, so loot that expired while the client was offline is gone from both.
        incoming = SaveGameRules.PruneExpiredStash(incoming, now);
        IReadOnlyList<string> changed = SaveGameRules.ServerOwnedChanges(current, incoming);
        if (changed.Count > 0)
        {
            return ApiErrors.Result(http, StatusCodes.Status409Conflict, ErrorCodes.SaveGameServerOwnedFieldChanged,
                "Server-owned fields cannot be changed by sync.",
                "Progression, currency, item contents and history change only through server rules (e.g. POST /api/v1/encounters/reward).",
                extensions: new Dictionary<string, object?> { ["fields"] = changed });
        }

        // Current HP/MP are client-reported but never above the class max (#71 decision 6): clamped, not rejected.
        SaveGameDocument next = character.ClampVitals(incoming with { Revision = expectedRevision + 1, UpdatedAtUtc = now, Mode = SaveGameRules.ModeServer });
        (SaveWriteStatus status, long currentRevision) = await saves.ReplaceAsync(next, expectedRevision, now, ct).ConfigureAwait(false);
        return status == SaveWriteStatus.RevisionConflict ? RevisionConflict(http, currentRevision) : SaveGameOk(http, next, time);
    }

    internal static async Task<IResult> ClaimEncounterRewardAsync(
        HttpContext http, ClaimEncounterRewardRequest? body, IEncounterStore encounters, IPlayerStore players, ISaveGameStore saves,
        ExpCurveTable curve, CharacterRules character, IOptions<PrototypeSaveGameDefaults> defaults, IOptions<SaveGameLimits> limitOptions,
        TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        SaveGameLimits limits = limitOptions.Value;
        var errors = new ValidationErrors();
        var overCaps = new ValidationErrors();
        RewardClaim? claim = null;
        if (body is null)
        {
            errors.Add("body", "Required.");
        }
        else
        {
            Validation.Id(errors, "encounterId", body.EncounterId);
            Validation.Id(errors, "playerId", body.PlayerId);
            Validation.Nonce(errors, "nonce", body.Nonce);
            claim = ValidateClaim(body.Claim, limits, character.Catalog, errors, overCaps);
        }

        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        EncounterSession? encounter = await encounters.GetAsync(body!.EncounterId!, ct).ConfigureAwait(false);
        if (encounter is null)
        {
            return ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.EncounterNotFound, "Encounter not found.",
                $"No encounter with id '{body.EncounterId}'.");
        }

        if (!string.Equals(encounter.PlayerId, body.PlayerId, StringComparison.Ordinal))
        {
            return ApiErrors.Result(http, StatusCodes.Status403Forbidden, ErrorCodes.EncounterPlayerMismatch,
                "Encounter belongs to a different player.");
        }

        string? storedHash = await encounters.GetNonceHashAsync(encounter.Id, ct).ConfigureAwait(false);
        if (storedHash is null || !NonceMatches(storedHash, body.Nonce!))
        {
            return ApiErrors.Result(http, StatusCodes.Status403Forbidden, ErrorCodes.EncounterNonceMismatch,
                "Encounter nonce does not match.", "Use the nonce from the most recent start response for this encounter.");
        }

        if (encounter.Status == EncounterStatus.Expired)
        {
            return ApiErrors.Result(http, StatusCodes.Status410Gone, ErrorCodes.EncounterExpired, "Encounter expired.",
                "An expired encounter never yields a reward.");
        }

        if (encounter.Status != EncounterStatus.Completed)
        {
            return ApiErrors.Result(http, StatusCodes.Status409Conflict, ErrorCodes.EncounterNotCompleted, "Encounter is not completed.",
                "Call POST /api/v1/encounters/complete first; the reward is tied to the server-side completion.");
        }

        if (overCaps.HasErrors)
        {
            // Not recorded: a corrected claim for the same encounter may still be applied.
            return ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.RewardClaimRejected,
                "Reward claim exceeds the prototype limits.", "The claim was not recorded.", overCaps.ToDictionary());
        }

        PlayerProfile? profile = await players.GetAsync(encounter.PlayerId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            return PlayerNotFound(http, encounter.PlayerId);
        }

        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        string claimJson = SaveGameJson.Serialize(claim!);
        SaveGameDocument initial = SaveGameRules.CreateInitial(profile, defaults.Value, curve, character);
        RewardStoreResult result;
        try
        {
            result = await saves.ApplyRewardAsync(
                encounter.Id, encounter.PlayerId, claimJson, initial,
                (document, nextRevision) => SaveGameRules.ApplyReward(SaveGameRules.PruneExpiredStash(document, now), claim!, encounter,
                    nextRevision, now, curve, character, limits.MaxEncounterResultsInDocument, Guid.NewGuid),
                now, ct).ConfigureAwait(false);
        }
        catch (SaveGameIntegrityException)
        {
            LogIntegrityFailure(loggers.CreateLogger(LogCategory), encounter.PlayerId);
            return IntegrityFailed(http);
        }

        ILogger log = loggers.CreateLogger(LogCategory);
        switch (result.Status)
        {
            case RewardStoreStatus.Rejected:
                return ApiErrors.Result(http, StatusCodes.Status409Conflict, ErrorCodes.RewardNoSpace, "No space for the reward.",
                    result.RejectionReason + " The reward was not recorded; free space and retry.");
            case RewardStoreStatus.AlreadyApplied:
                LogRewardReplay(log, encounter.Id, encounter.PlayerId);
                break;
            default:
                LogRewardApplied(log, encounter.Id, encounter.PlayerId, result.Document!.Revision);
                break;
        }

        SetEtag(http, result.Document!.Revision);
        return Results.Ok(new ClaimEncounterRewardResponse(
            Applied: result.Status == RewardStoreStatus.Applied,
            AlreadyApplied: result.Status == RewardStoreStatus.AlreadyApplied,
            ClaimMatchesApplied: string.Equals(result.StoredClaimJson, claimJson, StringComparison.Ordinal),
            Granted: result.Granted!,
            Revision: result.Document.Revision,
            SaveGame: result.Document));
    }

    private static RewardClaim? ValidateClaim(RewardClaimDto? claim, SaveGameLimits limits, ItemCatalog catalog, ValidationErrors errors, ValidationErrors overCaps)
    {
        if (claim is null)
        {
            errors.Add("claim", "Required.");
            return null;
        }

        void Amount(string field, long? value, long cap)
        {
            if (value is null)
            {
                errors.Add(field, "Required.");
            }
            else if (value < 0)
            {
                errors.Add(field, "Must be >= 0.");
            }
            else if (value > cap)
            {
                overCaps.Add(field, $"At most {cap.ToString(CultureInfo.InvariantCulture)} per encounter (prototype cap).");
            }
        }

        Amount("claim.exp", claim.Exp, limits.MaxRewardExpPerEncounter);
        Amount("claim.talers", claim.Talers, limits.MaxRewardTalersPerEncounter);

        // Catalog membership (#71 decision 8): stacks must be stackable non-material items, materials must be catalog
        // materials, items must be non-stacking catalog items. Quantities above a stack limit are split by ApplyReward.
        void Definition(string field, string? definitionId, bool? stackable, bool? material)
        {
            if (!SaveGameValidator.IsId(definitionId))
            {
                return;
            }

            ItemDefinition? definition = catalog.Find(definitionId);
            if (definition is null)
            {
                errors.Add(field, $"Unknown item '{definitionId}' (not in the item catalog).");
            }
            else if (stackable is bool s && definition.IsStackable != s)
            {
                errors.Add(field, s ? $"'{definitionId}' does not stack; claim it under items." : $"'{definitionId}' stacks; claim it under stacks or materials.");
            }
            else if (material is bool m && (definition.Category == ItemCatalog.CategoryMaterial) != m)
            {
                errors.Add(field, m ? $"'{definitionId}' is not a material; claim it under stacks." : $"'{definitionId}' is a material; claim it under materials.");
            }
        }

        List<SaveStackEntry> Stacks(string field, IReadOnlyList<RewardClaimStackDto>? list, bool material)
        {
            var result = new List<SaveStackEntry>();
            if (list is null)
            {
                errors.Add(field, "Required (may be empty).");
                return result;
            }

            if (list.Count > SaveGameRules.SchemaMaxRewardEntries)
            {
                errors.Add(field, $"At most {SaveGameRules.SchemaMaxRewardEntries} entries.");
            }
            else if (list.Count > limits.MaxRewardEntriesPerList)
            {
                overCaps.Add(field, $"At most {limits.MaxRewardEntriesPerList} entries (prototype cap).");
            }

            for (int i = 0; i < list.Count; i++)
            {
                string path = $"{field}[{i}]";
                RewardClaimStackDto? s = list[i];
                if (s is null)
                {
                    errors.Add(path, "Required.");
                    continue;
                }

                SaveGameValidator.Id(errors, path + ".definitionId", s.DefinitionId);
                Definition(path + ".definitionId", s.DefinitionId, stackable: true, material: material);
                if (s.Quantity is null or < 1)
                {
                    errors.Add(path + ".quantity", "Must be >= 1.");
                }
                else if (s.Quantity > limits.MaxRewardQuantityPerEntry)
                {
                    overCaps.Add(path + ".quantity", $"At most {limits.MaxRewardQuantityPerEntry} (prototype cap).");
                }
                else if (s.DefinitionId is not null)
                {
                    result.Add(new SaveStackEntry { DefinitionId = s.DefinitionId, Quantity = s.Quantity.Value });
                }
            }

            return result;
        }

        List<SaveStackEntry> stacks = Stacks("claim.stacks", claim.Stacks, material: false);
        List<SaveStackEntry> materials = Stacks("claim.materials", claim.Materials, material: true);
        var items = new List<RewardClaimItem>();
        if (claim.Items is null)
        {
            errors.Add("claim.items", "Required (may be empty).");
        }
        else
        {
            if (claim.Items.Count > SaveGameRules.SchemaMaxRewardEntries)
            {
                errors.Add("claim.items", $"At most {SaveGameRules.SchemaMaxRewardEntries} entries.");
            }
            else if (claim.Items.Count > limits.MaxRewardEntriesPerList)
            {
                overCaps.Add("claim.items", $"At most {limits.MaxRewardEntriesPerList} entries (prototype cap).");
            }

            for (int i = 0; i < claim.Items.Count; i++)
            {
                string path = $"claim.items[{i}]";
                RewardClaimItemDto? item = claim.Items[i];
                if (item is null)
                {
                    errors.Add(path, "Required.");
                    continue;
                }

                SaveGameValidator.Id(errors, path + ".definitionId", item.DefinitionId);
                Definition(path + ".definitionId", item.DefinitionId, stackable: false, material: null);
                if (item.ItemLevel is null or < 1 or > 99)
                {
                    errors.Add(path + ".itemLevel", "Must be 1-99.");
                }

                SaveGameValidator.Rarity(errors, path + ".rarity", item.Rarity);
                if (item.DefinitionId is not null && item.ItemLevel is not null && item.Rarity is not null)
                {
                    items.Add(new RewardClaimItem(item.DefinitionId, item.ItemLevel.Value, item.Rarity));
                }
            }
        }

        return new RewardClaim(claim.Exp ?? 0, claim.Talers ?? 0, stacks, items, materials);
    }

    /// <summary>Read the PUT body: size limit, JSON object, schemaVersion check + migration, typed deserialization.</summary>
    private static async Task<(SaveGameDocument? Document, IResult? Failure)> ReadDocumentAsync(
        HttpContext http, SaveGameMigrator migrator, SaveGameLimits limits, CancellationToken ct)
    {
        if (http.Request.ContentLength > limits.MaxBodyBytes)
        {
            return (null, PayloadTooLarge(http, limits));
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limits.MaxBodyBytes)
            {
                return (null, PayloadTooLarge(http, limits));
            }

            buffer.Write(chunk, 0, read);
        }

        JsonObject? json;
        try
        {
            json = buffer.Length == 0 ? null : JsonNode.Parse(buffer.ToArray()) as JsonObject;
        }
        catch (JsonException)
        {
            return (null, ApiErrors.Result(http, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest,
                "The request could not be read.", "Malformed JSON body."));
        }

        if (json is null)
        {
            return (null, ApiErrors.Result(http, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest,
                "The request could not be read.", "The body must be a SaveGame JSON object."));
        }

        if (json["schemaVersion"] is not JsonValue versionNode || !versionNode.TryGetValue(out int version))
        {
            var errors = new ValidationErrors();
            errors.Add("schemaVersion", "Required integer.");
            return (null, ApiErrors.Validation(http, errors.ToDictionary()));
        }

        SaveGameVersionCheck check = migrator.Upgrade(json, version, out JsonObject upgraded);
        if (check is SaveGameVersionCheck.TooNew or SaveGameVersionCheck.TooOld)
        {
            return (null, ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.SaveGameSchemaUnsupported,
                "Unsupported SaveGame schema version.",
                check == SaveGameVersionCheck.TooNew
                    ? "The save was written by a newer client; this server cannot read it and will not overwrite it."
                    : "The save is older than any version this server can migrate.",
                extensions: new Dictionary<string, object?>
                {
                    ["receivedSchemaVersion"] = version,
                    ["supportedSchemaVersion"] = migrator.CurrentVersion,
                }));
        }

        try
        {
            SaveGameDocument? document = upgraded.Deserialize<SaveGameDocument>(SaveGameJson.Options);
            return document is null
                ? (null, ApiErrors.Result(http, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest, "The request could not be read.", "Malformed JSON body."))
                : (document, null);
        }
        catch (JsonException ex)
        {
            return (null, ApiErrors.Result(http, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest,
                "The request could not be read.", ServerSetup.DescribeJsonException(ex)));
        }
    }

    private static async Task<(StoredSaveGame? Stored, IResult? Failure)> LoadAsync(
        HttpContext http, ISaveGameStore saves, string playerId, ILoggerFactory loggers, CancellationToken ct)
    {
        try
        {
            return (await saves.GetAsync(playerId, ct).ConfigureAwait(false), null);
        }
        catch (SaveGameIntegrityException)
        {
            LogIntegrityFailure(loggers.CreateLogger(LogCategory), playerId);
            return (null, IntegrityFailed(http));
        }
    }

    private static IResult SaveGameOk(HttpContext http, SaveGameDocument document, TimeProvider time)
    {
        SetEtag(http, document.Revision);
        return Results.Ok(new SaveGameResponse(document.Revision, document, time.GetUtcNow()));
    }

    private static void SetEtag(HttpContext http, long revision) =>
        http.Response.Headers.ETag = "\"" + revision.ToString(CultureInfo.InvariantCulture) + "\"";

    private static IResult PlayerNotFound(HttpContext http, string playerId) =>
        ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.PlayerNotFound, "Player not found.", $"No player with id '{playerId}'.");

    private static IResult RevisionConflict(HttpContext http, long currentRevision) =>
        ApiErrors.Result(http, StatusCodes.Status412PreconditionFailed, ErrorCodes.SaveGameRevisionConflict,
            "The save changed since you read it.", "GET the current save, re-apply your local changes and retry with its ETag.",
            extensions: new Dictionary<string, object?> { ["currentRevision"] = currentRevision });

    private static IResult IntegrityFailed(HttpContext http) =>
        ApiErrors.Result(http, StatusCodes.Status500InternalServerError, ErrorCodes.SaveGameIntegrityFailed,
            "The stored save failed its integrity check.",
            "Nothing was overwritten; the revision history is kept for manual recovery. Keep your local copy and report the traceId.");

    private static IResult PayloadTooLarge(HttpContext http, SaveGameLimits limits) =>
        ApiErrors.Result(http, StatusCodes.Status413PayloadTooLarge, ErrorCodes.PayloadTooLarge, "The SaveGame is too large.",
            $"At most {limits.MaxBodyBytes.ToString(CultureInfo.InvariantCulture)} bytes.");

    [LoggerMessage(EventId = 110, Level = LogLevel.Error, Message = "Stored SaveGame failed its integrity check for player={PlayerId}; left untouched")]
    private static partial void LogIntegrityFailure(ILogger logger, string playerId);

    [LoggerMessage(EventId = 111, Level = LogLevel.Information, Message = "Encounter reward applied encounter={EncounterId} player={PlayerId} revision={Revision}")]
    private static partial void LogRewardApplied(ILogger logger, string encounterId, string playerId, long revision);

    [LoggerMessage(EventId = 112, Level = LogLevel.Information, Message = "Encounter reward replay (already applied) encounter={EncounterId} player={PlayerId}")]
    private static partial void LogRewardReplay(ILogger logger, string encounterId, string playerId);
}
