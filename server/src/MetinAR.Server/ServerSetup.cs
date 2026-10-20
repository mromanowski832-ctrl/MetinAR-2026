using System.Text.Json;
using System.Text.Json.Serialization;
using MetinAR.Server.Api;
using Microsoft.Data.Sqlite;

namespace MetinAR.Server;

internal static class ServerSetup
{
    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        // Unknown fields (e.g. a client trying to set "level") are rejected instead of silently ignored.
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
    }

    public static string ResolveSqlitePath(string connectionString, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        string source = builder.DataSource;
        if (!string.IsNullOrEmpty(source) && source != ":memory:"
            && !source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(source))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, source));
        }

        return builder.ToString();
    }

    /// <summary>Client-facing description that never exposes server type names or stack details.</summary>
    public static string DescribeBadRequest(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException json)
        {
            const string marker = "The JSON property '";
            int start = json.Message.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0 && json.Message.Contains("could not be mapped", StringComparison.Ordinal))
            {
                start += marker.Length;
                int end = json.Message.IndexOf('\'', start);
                return $"Unknown field '{json.Message[start..end]}'. Only documented fields are accepted.";
            }

            return string.IsNullOrEmpty(json.Path)
                ? "Malformed JSON body."
                : $"Malformed JSON or wrong value type at '{json.Path}'.";
        }

        // Query/route binding failures, e.g. lat=abc. The framework message names only the parameter.
        const string bindMarker = "Failed to bind parameter \"";
        if (exception.Message.StartsWith(bindMarker, StringComparison.Ordinal))
        {
            int end = exception.Message.IndexOf('"', bindMarker.Length);
            string declaration = end > 0 ? exception.Message[bindMarker.Length..end] : string.Empty;
            string name = declaration[(declaration.LastIndexOf(' ') + 1)..];
            return name.Length > 0
                ? $"Parameter '{name}' has an invalid format."
                : "A query or route parameter has an invalid format.";
        }

        return "The request could not be read.";
    }
}
