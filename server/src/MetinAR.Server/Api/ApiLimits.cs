namespace MetinAR.Server.Api;

/// <summary>Technical request limits (not gameplay values). Configurable via the <c>ApiLimits</c> section.</summary>
public sealed class ApiLimits
{
    public const string SectionName = "ApiLimits";

    public double MinRadiusMeters { get; set; } = 1;
    public double MaxRadiusMeters { get; set; } = 1000;
    public double DefaultRadiusMeters { get; set; } = 250;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
    public int MaxOffset { get; set; } = 10_000;
    public int MaxDisplayNameLength { get; set; } = 32;
}
