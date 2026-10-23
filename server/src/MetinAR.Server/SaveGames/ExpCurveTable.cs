using System.Globalization;
using System.Reflection;

namespace MetinAR.Server.SaveGames;

/// <summary>
/// The approved EXP curve, read from <c>docs/design/tables/exp_curve.csv</c> (embedded at build time, single source of
/// truth shared with the client's <c>ExpCurve</c>). Level is derived from total EXP and capped at the max level.
/// </summary>
public sealed class ExpCurveTable
{
    public const string ResourceName = "MetinAR.Server.Design.exp_curve.csv";

    private readonly long[] _cumulativeAtLevelStart; // index = level (1-based); [0] unused

    public ExpCurveTable(IReadOnlyList<long> cumulativeExpAtLevelStart)
    {
        ArgumentNullException.ThrowIfNull(cumulativeExpAtLevelStart);
        if (cumulativeExpAtLevelStart.Count < 1 || cumulativeExpAtLevelStart[0] != 0)
        {
            throw new ArgumentException("Level 1 must start at 0 EXP.", nameof(cumulativeExpAtLevelStart));
        }

        _cumulativeAtLevelStart = new long[cumulativeExpAtLevelStart.Count + 1];
        for (int i = 0; i < cumulativeExpAtLevelStart.Count; i++)
        {
            if (i > 0 && cumulativeExpAtLevelStart[i] <= cumulativeExpAtLevelStart[i - 1])
            {
                throw new ArgumentException("Cumulative EXP must strictly increase.", nameof(cumulativeExpAtLevelStart));
            }

            _cumulativeAtLevelStart[i + 1] = cumulativeExpAtLevelStart[i];
        }
    }

    public int MaxLevel => _cumulativeAtLevelStart.Length - 1;

    /// <summary>Total EXP at the start of the max level; total EXP is capped here (same as the client).</summary>
    public long TotalExpToMaxLevel => _cumulativeAtLevelStart[MaxLevel];

    public long CumulativeExpAtLevelStart(int level) => _cumulativeAtLevelStart[level];

    public int LevelForTotalExp(long totalExp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalExp);
        int level = 1;
        while (level < MaxLevel && _cumulativeAtLevelStart[level + 1] <= totalExp)
        {
            level++;
        }

        return level;
    }

    public static ExpCurveTable LoadEmbedded()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static ExpCurveTable Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] header = lines[0].Split(',');
        int levelColumn = Array.IndexOf(header, "level");
        int cumulativeColumn = Array.IndexOf(header, "cumulativeExpAtLevelStart");
        if (levelColumn < 0 || cumulativeColumn < 0)
        {
            throw new FormatException("exp_curve.csv needs 'level' and 'cumulativeExpAtLevelStart' columns.");
        }

        var cumulative = new List<long>();
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            int level = int.Parse(cells[levelColumn], CultureInfo.InvariantCulture);
            if (level != cumulative.Count + 1)
            {
                throw new FormatException($"exp_curve.csv levels must be consecutive from 1 (found {level}).");
            }

            cumulative.Add(long.Parse(cells[cumulativeColumn], CultureInfo.InvariantCulture));
        }

        return new ExpCurveTable(cumulative);
    }
}
