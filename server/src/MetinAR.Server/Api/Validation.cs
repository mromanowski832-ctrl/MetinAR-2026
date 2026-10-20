using System.Text.RegularExpressions;

namespace MetinAR.Server.Api;

internal static partial class Validation
{
    [GeneratedRegex("^[A-Za-z0-9_-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? value) => value is not null && IdPattern().IsMatch(value);

    public const string IdRule = "Must be 3-64 characters of A-Z, a-z, 0-9, '_' or '-'.";

    public static void Id(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(field, "Required.");
        }
        else if (!IsValidId(value))
        {
            errors.Add(field, IdRule);
        }
    }

    public static void Position(ValidationErrors errors, string field, GeoPositionDto? position)
    {
        if (position is null)
        {
            return;
        }

        if (double.IsNaN(position.Latitude) || position.Latitude is < -90 or > 90)
        {
            errors.Add(field + ".latitude", "Must be between -90 and 90.");
        }

        if (double.IsNaN(position.Longitude) || position.Longitude is < -180 or > 180)
        {
            errors.Add(field + ".longitude", "Must be between -180 and 180.");
        }
    }
}
