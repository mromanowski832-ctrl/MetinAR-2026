namespace MetinAR.Server.Domain;

/// <summary>WGS84 latitude/longitude in decimal degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public static bool IsValid(double latitude, double longitude) =>
        !double.IsNaN(latitude) && !double.IsNaN(longitude)
        && latitude is >= -90.0 and <= 90.0
        && longitude is >= -180.0 and <= 180.0;
}
