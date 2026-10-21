using MetinAR.Server;
using MetinAR.Server.Api;
using MetinAR.Server.Domain;
using MetinAR.Server.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiLimits>(builder.Configuration.GetSection(ApiLimits.SectionName));
builder.Services.Configure<PrototypePlayerDefaults>(builder.Configuration.GetSection(PrototypePlayerDefaults.SectionName));
builder.Services.AddOptions<EncounterRules>()
    .Bind(builder.Configuration.GetSection(EncounterRules.SectionName))
    .Validate(r => !r.Validate().Any(), "EncounterRules configuration is invalid (see EncounterRules.Validate).")
    .ValidateOnStart();
builder.Services.TryAddSingleton(TimeProvider.System);

string connectionString = builder.Configuration.GetConnectionString("MetinAR")
    ?? throw new InvalidOperationException("ConnectionStrings:MetinAR is not configured.");
// Relative SQLite paths resolve against the content root, not the shell's working directory.
connectionString = ServerSetup.ResolveSqlitePath(connectionString, builder.Environment.ContentRootPath);

builder.Services.AddSingleton(new SqliteConnectionFactory(connectionString));
builder.Services.AddSingleton<SchemaMigrator>();
builder.Services.AddSingleton<SqliteGameStore>();
builder.Services.AddSingleton<IPlayerStore>(sp => sp.GetRequiredService<SqliteGameStore>());
builder.Services.AddSingleton<IWorldStore>(sp => sp.GetRequiredService<SqliteGameStore>());
builder.Services.AddSingleton<IEncounterStore>(sp => sp.GetRequiredService<SqliteGameStore>());

builder.Services.Configure<JsonOptions>(o => ServerSetup.ConfigureJson(o.SerializerOptions));
// Malformed JSON / unbindable query values become BadHttpRequestException, which is mapped to problem+json below.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MetinAR prototype API",
        Version = "v1",
        Description = "METINAR-010 local development backend. No authentication: prototype only, never deploy publicly.",
    });
    o.MapType<DateTimeOffset>(() => new OpenApiSchema { Type = "string", Format = "date-time", Example = new Microsoft.OpenApi.Any.OpenApiString("2026-01-01T00:00:00.000Z") });
});

WebApplication app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(context =>
{
    Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    return error is BadHttpRequestException bad
        ? ApiErrors.WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest,
            "The request could not be read.", ServerSetup.DescribeBadRequest(bad))
        : ApiErrors.WriteAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
            "Unexpected server error.");
}));

app.UseStatusCodePages(context =>
{
    HttpContext http = context.HttpContext;
    return http.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => ApiErrors.WriteAsync(http, 404, ErrorCodes.NotFound, "No such route."),
        StatusCodes.Status405MethodNotAllowed => ApiErrors.WriteAsync(http, 405, ErrorCodes.MethodNotAllowed, "Method not allowed for this route."),
        _ => Task.CompletedTask,
    };
});

if (app.Configuration.GetValue("Api:EnableSwagger", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "MetinAR v1"));
}

await app.Services.GetRequiredService<SchemaMigrator>().MigrateAsync().ConfigureAwait(false);

app.MapMetinArApi();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point marker so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
