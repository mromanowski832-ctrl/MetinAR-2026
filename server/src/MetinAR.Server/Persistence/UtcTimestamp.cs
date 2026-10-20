using System.Globalization;

namespace MetinAR.Server.Persistence;

/// <summary>Single canonical timestamp format for storage and the wire: UTC, millisecond precision, 'Z' suffix.</summary>
public static class UtcTimestamp
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string ToText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Truncate to millisecond precision so values round-trip exactly through storage.</summary>
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }
}
