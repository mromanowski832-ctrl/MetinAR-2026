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
            return DescribeJsonException(json);
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

    /// <summary>Client-facing description of a JSON read failure (unknown field, missing field, wrong type).</summary>
    public static string DescribeJsonException(JsonException json)
    {
        ArgumentNullException.ThrowIfNull(json);
        const string marker = "The JSON property '";
        int start = json.Message.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0 && json.Message.Contains("could not be mapped", StringComparison.Ordinal))
        {
            start += marker.Length;
            int end = json.Message.IndexOf('\'', start);
            return $"Unknown field '{json.Message[start..end]}'. Only documented fields are accepted.";
        }

        const string missingMarker = "missing required properties, including the following: ";
        int missing = json.Message.IndexOf(missingMarker, StringComparison.Ordinal);
        if (missing >= 0)
        {
            string names = json.Message[(missing + missingMarker.Length)..].Trim();
            string at = string.IsNullOrEmpty(json.Path) ? string.Empty : $" at '{json.Path}'";
            return $"Missing required field(s){at}: {names}.";
        }

        return string.IsNullOrEmpty(json.Path)
            ? "Malformed JSON body."
            : $"Malformed JSON or wrong value type at '{json.Path}'.";
    }
}
