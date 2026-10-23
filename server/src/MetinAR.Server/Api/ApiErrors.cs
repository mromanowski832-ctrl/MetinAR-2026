using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MetinAR.Server.Api;

/// <summary>Stable machine-readable error codes. Clients switch on <c>code</c>, never on <c>title</c>/<c>detail</c>.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string MalformedRequest = "MALFORMED_REQUEST";
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";
    public const string WorldEntityNotFound = "WORLD_ENTITY_NOT_FOUND";
    public const string WorldEntityInactive = "WORLD_ENTITY_INACTIVE";
    public const string EncounterNotFound = "ENCOUNTER_NOT_FOUND";
    public const string EncounterPlayerMismatch = "ENCOUNTER_PLAYER_MISMATCH";
    public const string EncounterNonceMismatch = "ENCOUNTER_NONCE_MISMATCH";
    public const string EncounterExpired = "ENCOUNTER_EXPIRED";
    public const string EncounterTooFar = "ENCOUNTER_TOO_FAR";
    public const string GpsAccuracyInsufficient = "GPS_ACCURACY_INSUFFICIENT";
    public const string GpsFixStale = "GPS_FIX_STALE";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string InternalError = "INTERNAL_ERROR";

    // SaveGame (#71)
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string SaveGameSchemaUnsupported = "SAVEGAME_SCHEMA_UNSUPPORTED";
    public const string SaveGameLocalModeNotSyncable = "SAVEGAME_LOCAL_MODE_NOT_SYNCABLE";
    public const string SaveGamePreconditionRequired = "SAVEGAME_PRECONDITION_REQUIRED";
    public const string SaveGameRevisionConflict = "SAVEGAME_REVISION_CONFLICT";
    public const string SaveGameServerOwnedFieldChanged = "SAVEGAME_SERVER_OWNED_FIELD_CHANGED";
    public const string SaveGameIntegrityFailed = "SAVEGAME_INTEGRITY_FAILED";
    public const string EncounterNotCompleted = "ENCOUNTER_NOT_COMPLETED";
    public const string RewardClaimRejected = "REWARD_CLAIM_REJECTED";
    public const string RewardNoSpace = "REWARD_NO_SPACE";
}

/// <summary>Builds RFC 9457 problem+json responses with the MetinAR <c>code</c> and <c>traceId</c> extensions.</summary>
public static class ApiErrors
{
    public static ProblemDetails Create(HttpContext context, int status, string code, string title, string? detail = null,
        IDictionary<string, string[]>? errors = null, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = new ProblemDetails
        {
            Type = "https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/main/docs/METINAR-010-BACKEND.md#error-codes",
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        if (extensions is not null)
        {
            foreach ((string key, object? value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    public static IResult Result(HttpContext context, int status, string code, string title, string? detail = null,
        IDictionary<string, string[]>? errors = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        Results.Problem(Create(context, status, code, title, detail, errors, extensions));

    public static IResult Validation(HttpContext context, IDictionary<string, string[]> errors) =>
        Result(context, StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "One or more request fields are invalid.", errors: errors);

    public static Task WriteAsync(HttpContext context, int status, string code, string title, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Create(context, status, code, title, detail), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

/// <summary>Accumulates field errors in the problem+json <c>errors</c> shape.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out List<string>? list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public IDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
}
