using System.Text.Json;
using System.Text.Json.Serialization;
using MetinAR.Server.Persistence;

namespace MetinAR.Server.Api;

/// <summary>Writes every timestamp as UTC ISO-8601 with millisecond precision and a 'Z' suffix.</summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(UtcTimestamp.ToText(value));
    }
}
