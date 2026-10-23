using System.Security.Cryptography;
using System.Text;
using MetinAR.Server.Domain;
using MetinAR.Server.Geo;
using MetinAR.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MetinAR.Server.Api;

public static partial class Endpoints
{
    public const string ApiVersion = "v1";

    public static void MapMetinArApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", HealthAsync)
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<HealthResponse>()
            .Produces(StatusCodes.Status503ServiceUnavailable);

        RouteGroupBuilder v1 = app.MapGroup("/api/" + ApiVersion);

        v1.MapGet("/player/{playerId}", GetPlayerAsync)
            .WithName("GetPlayer").WithTags("Player")
            .Produces<PlayerProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        v1.MapPut("/player/{playerId}", PutPlayerAsync)
            .WithName("PutPlayer").WithTags("Player")
            .Accepts<PutPlayerRequest>("application/json")
            .Produces<PlayerProfileDto>(StatusCodes.Status200OK)
            .Produces<PlayerProfileDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        v1.MapGet("/world/nearby", GetNearbyAsync)
            .WithName("GetNearbyWorldEntities").WithTags("World")
            .Produces<NearbyWorldResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        v1.MapPost("/encounters/start", StartEncounterAsync)
            .WithName("StartEncounter").WithTags("Encounters")
            .Accepts<StartEncounterRequest>("application/json")
            .Produces<StartEncounterResponse>(StatusCodes.Status201Created)
            .Produces<StartEncounterResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        v1.MapPost("/encounters/complete", CompleteEncounterAsync)
            .WithName("CompleteEncounter").WithTags("Encounters")
            .Accepts<CompleteEncounterRequest>("application/json")
            .Produces<CompleteEncounterResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        MapSaveGameApi(v1);
    }

    internal static async Task<IResult> HealthAsync(SqliteConnectionFactory connections, TimeProvider time, CancellationToken ct)
    {
        try
        {
            await using SqliteConnection connection = await connections.OpenAsync(ct).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(id) FROM schema_migrations;";
            object? schema = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Results.Ok(new HealthResponse("ok", time.GetUtcNow(), ApiVersion, schema as string));
        }
        catch (SqliteException)
        {
            return Results.Json(new HealthResponse("degraded", time.GetUtcNow(), ApiVersion, null), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static async Task<IResult> GetPlayerAsync(HttpContext http, string playerId, IPlayerStore players, CancellationToken ct)
    {
        if (!Validation.IsValidId(playerId))
        {
            var errors = new ValidationErrors();
            errors.Add("playerId", Validation.IdRule);
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        PlayerProfile? profile = await players.GetAsync(playerId, ct).ConfigureAwait(false);
        return profile is null
            ? ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.PlayerNotFound, "Player not found.", $"No player with id '{playerId}'.")
            : Results.Ok(profile.ToDto());
    }

    internal static async Task<IResult> PutPlayerAsync(
        HttpContext http, string playerId, PutPlayerRequest? body, IPlayerStore players,
        IOptions<ApiLimits> limits, IOptions<PrototypePlayerDefaults> defaults, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Validation.Id(errors, "playerId", playerId);
        string? displayName = body?.DisplayName?.Trim();
        if (body is null)
        {
            errors.Add("body", "Required.");
        }
        else if (string.IsNullOrEmpty(displayName))
        {
            errors.Add("displayName", "Required.");
        }
        else if (displayName.Length > limits.Value.MaxDisplayNameLength || displayName.Any(char.IsControl))
        {
            errors.Add("displayName", $"Must be 1-{limits.Value.MaxDisplayNameLength} printable characters.");
        }

        Validation.Position(errors, "lastKnownPosition", body?.LastKnownPosition);
        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        // Client-reported location is reduced to ~110 m precision before storage.
        GeoPoint? coarse = body!.LastKnownPosition is { } p
            ? GeoDistance.Coarsen(new GeoPoint(p.Latitude, p.Longitude))
            : null;

        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        (PlayerProfile profile, bool created) = await players.UpsertAsync(playerId, displayName!, coarse, defaults.Value, now, ct).ConfigureAwait(false);
        return created
            ? Results.Created($"/api/{ApiVersion}/player/{profile.Id}", profile.ToDto())
            : Results.Ok(profile.ToDto());
    }

    internal static async Task<IResult> GetNearbyAsync(
        HttpContext http, double? lat, double? lon, double? radiusMeters, int? limit, int? offset,
        IWorldStore world, IOptions<ApiLimits> limitOptions, TimeProvider time, CancellationToken ct)
    {
        ApiLimits limits = limitOptions.Value;
        var errors = new ValidationErrors();
        if (lat is null)
        {
            errors.Add("lat", "Required.");
        }

        if (lon is null)
        {
            errors.Add("lon", "Required.");
        }

        if (lat is not null || lon is not null)
        {
            Validation.Position(errors, "query", new GeoPositionDto(lat ?? 0, lon ?? 0));
        }

        double radius = radiusMeters ?? limits.DefaultRadiusMeters;
        if (double.IsNaN(radius) || radius < limits.MinRadiusMeters || radius > limits.MaxRadiusMeters)
        {
            errors.Add("radiusMeters", $"Must be between {limits.MinRadiusMeters} and {limits.MaxRadiusMeters}.");
        }

        int pageSize = limit ?? limits.DefaultPageSize;
        if (pageSize < 1 || pageSize > limits.MaxPageSize)
        {
            errors.Add("limit", $"Must be between 1 and {limits.MaxPageSize}.");
        }

        int skip = offset ?? 0;
        if (skip < 0 || skip > limits.MaxOffset)
        {
            errors.Add("offset", $"Must be between 0 and {limits.MaxOffset}.");
        }

        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        var center = new GeoPoint(lat!.Value, lon!.Value);
        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        var box = GeoDistance.BoundingBox(center, radius);
        IReadOnlyList<WorldEntity> candidates = await world.ListActiveInBoxAsync(
            box.MinLat, box.MaxLat, box.MinLon, box.MaxLon, box.CrossesAntimeridianOrPole, now, ct).ConfigureAwait(false);

        // Exact radius filter, then a deterministic order: nearest first, ties broken by stable id.
        var inRadius = candidates
            .Where(e => e.IsActiveAt(now)) // schedule phase (deterministic, server-owned)
            .Select(e => (Entity: e, Distance: GeoDistance.HaversineMeters(center, new GeoPoint(e.Latitude, e.Longitude))))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Entity.Id, StringComparer.Ordinal)
            .ToList();

        var page = inRadius.Skip(skip).Take(pageSize).Select(x => x.Entity.ToDto(now, x.Distance)).ToList();
        int? next = skip + page.Count < inRadius.Count ? skip + page.Count : null;

        return Results.Ok(new NearbyWorldResponse(
            new GeoPositionDto(center.Latitude, center.Longitude), radius, now, skip, pageSize, inRadius.Count, next, page));
    }

    internal static async Task<IResult> StartEncounterAsync(
        HttpContext http, StartEncounterRequest? body, IPlayerStore players, IWorldStore world, IEncounterStore encounters,
        IOptions<EncounterRules> ruleOptions, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        EncounterRules rules = ruleOptions.Value;
        var errors = new ValidationErrors();
        if (body is null)
        {
            errors.Add("body", "Required.");
        }
        else
        {
            Validation.Id(errors, "playerId", body.PlayerId);
            Validation.Id(errors, "worldEntityId", body.WorldEntityId);
            Validation.ClientFix(errors, "clientPosition", body.ClientPosition);
        }

        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        if (await players.GetAsync(body!.PlayerId!, ct).ConfigureAwait(false) is null)
        {
            return ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.PlayerNotFound, "Player not found.",
                $"No player with id '{body.PlayerId}'. Create it with PUT /api/{ApiVersion}/player/{{playerId}} first.");
        }

        WorldEntity? entity = await world.GetAsync(body.WorldEntityId!, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.WorldEntityNotFound, "World entity not found.",
                $"No world entity with id '{body.WorldEntityId}'.");
        }

        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        if (!entity.IsActiveAt(now))
        {
            return ApiErrors.Result(http, StatusCodes.Status409Conflict, ErrorCodes.WorldEntityInactive, "World entity is not active.",
                $"World entity '{entity.Id}' is inactive, outside its spawn window or outside its scheduled active phase.");
        }

        // Server-side proximity authority: the entity position is server-owned; the client fix is only evidence.
        ClientFixDto fix = body.ClientPosition!;
        (ProximityVerdict verdict, EncounterStartEvidence evidence) = ProximityCheck.Evaluate(
            new GeoPoint(fix.Latitude!.Value, fix.Longitude!.Value), fix.HorizontalAccuracyMeters!.Value, fix.CapturedAt,
            new GeoPoint(entity.Latitude, entity.Longitude), now, rules);

        if (verdict != ProximityVerdict.Allowed)
        {
            ILogger log = loggers.CreateLogger("MetinAR.Server.Encounters");
            // Never log client coordinates: only the verdict and rounded server-side derived values.
            LogStartRejected(log, verdict.ToString(), body.PlayerId!, entity.Id, Math.Round(evidence.DistanceMeters), Math.Round(evidence.AccuracyMeters));
            return verdict switch
            {
                ProximityVerdict.AccuracyTooPoor => ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.GpsAccuracyInsufficient,
                    "GPS accuracy is too poor to start an encounter.",
                    $"Reported accuracy must be at most {rules.MaxAcceptedAccuracyMeters} m.",
                    extensions: new Dictionary<string, object?> { ["maxAccuracyMeters"] = rules.MaxAcceptedAccuracyMeters }),
                ProximityVerdict.FixStale or ProximityVerdict.FixInFuture => ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.GpsFixStale,
                    "GPS fix timestamp is not current.",
                    verdict == ProximityVerdict.FixStale
                        ? $"capturedAt is older than {rules.MaxFixAgeSeconds} s."
                        : $"capturedAt is more than {rules.MaxFutureSkewSeconds} s in the future.",
                    extensions: new Dictionary<string, object?> { ["maxFixAgeSeconds"] = rules.MaxFixAgeSeconds }),
                _ => ApiErrors.Result(http, StatusCodes.Status422UnprocessableEntity, ErrorCodes.EncounterTooFar,
                    "Too far from the world entity to start an encounter.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["distanceMeters"] = Math.Round(evidence.DistanceMeters),
                        ["allowedMeters"] = Math.Round(evidence.AllowedMeters, 1),
                    }),
            };
        }

        string nonce = NewNonce();
        (EncounterSession session, bool created) = await encounters.StartAsync(
            Guid.NewGuid().ToString("N"), body.PlayerId!, entity.Id, now, now.AddSeconds(rules.EncounterTtlSeconds),
            HashNonce(nonce), evidence, ct).ConfigureAwait(false);

        var response = new StartEncounterResponse(session.ToDto(), created, nonce);
        return created ? Results.Created($"/api/{ApiVersion}/encounters/{session.Id}", response) : Results.Ok(response);
    }

    internal static async Task<IResult> CompleteEncounterAsync(
        HttpContext http, CompleteEncounterRequest? body, IEncounterStore encounters, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (body is null)
        {
            errors.Add("body", "Required.");
        }
        else
        {
            Validation.Id(errors, "encounterId", body.EncounterId);
            Validation.Id(errors, "playerId", body.PlayerId);
            Validation.Nonce(errors, "nonce", body.Nonce);
        }

        if (errors.HasErrors)
        {
            return ApiErrors.Validation(http, errors.ToDictionary());
        }

        EncounterSession? existing = await encounters.GetAsync(body!.EncounterId!, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.EncounterNotFound, "Encounter not found.",
                $"No encounter with id '{body.EncounterId}'.");
        }

        if (!string.Equals(existing.PlayerId, body.PlayerId, StringComparison.Ordinal))
        {
            return ApiErrors.Result(http, StatusCodes.Status403Forbidden, ErrorCodes.EncounterPlayerMismatch,
                "Encounter belongs to a different player.");
        }

        string? storedHash = await encounters.GetNonceHashAsync(existing.Id, ct).ConfigureAwait(false);
        if (storedHash is null || !NonceMatches(storedHash, body.Nonce!))
        {
            return ApiErrors.Result(http, StatusCodes.Status403Forbidden, ErrorCodes.EncounterNonceMismatch,
                "Encounter nonce does not match.", "Use the nonce from the most recent start response for this encounter.");
        }

        DateTimeOffset now = UtcTimestamp.Normalize(time.GetUtcNow());
        (EncounterSession Session, CompletionOutcome Outcome)? result = await encounters.CompleteAsync(existing.Id, now, ct).ConfigureAwait(false);
        if (result is null)
        {
            return ApiErrors.Result(http, StatusCodes.Status404NotFound, ErrorCodes.EncounterNotFound, "Encounter not found.");
        }

        if (result.Value.Outcome == CompletionOutcome.Expired)
        {
            return ApiErrors.Result(http, StatusCodes.Status410Gone, ErrorCodes.EncounterExpired, "Encounter has expired.",
                $"The encounter expired at {UtcTimestamp.ToText(result.Value.Session.ExpiresAt)}. Start a new one.");
        }

        // Future rewards may ONLY be granted when FirstCompletion is true (see docs, "Idempotency").
        return Results.Ok(new CompleteEncounterResponse(result.Value.Session.ToDto(), result.Value.Outcome == CompletionOutcome.FirstCompletion));
    }

    internal static string NewNonce() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static string HashNonce(string nonce) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));

    private static bool NonceMatches(string storedHashHex, string nonce) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedHashHex), Encoding.ASCII.GetBytes(HashNonce(nonce)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "Encounter start rejected: {Verdict} player={PlayerId} entity={WorldEntityId} distanceM={DistanceMeters} accuracyM={AccuracyMeters}")]
    private static partial void LogStartRejected(ILogger logger, string verdict, string playerId, string worldEntityId, double distanceMeters, double accuracyMeters);
}
