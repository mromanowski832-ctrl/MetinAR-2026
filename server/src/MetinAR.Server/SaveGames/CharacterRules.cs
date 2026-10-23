using System.Globalization;
using System.Reflection;

namespace MetinAR.Server.SaveGames;

/// <summary>Start values and per-level growth of a class (CHARACTER_PROGRESSION §1.2).</summary>
public sealed record ClassDefinition(
    string Id,
    SavePrimaryStats Start,
    string AutoStatA,
    string AutoStatB,
    int HpPerLevel,
    int MpPerLevel);

/// <summary>
/// Server-side character rules from CHARACTER_PROGRESSION (#71 decisions 3 and 6): level-up point grants and the
/// max HP/MP clamp. The class table mirrors CHARACTER_PROGRESSION §1.2 (no machine-readable copy exists; a test parses
/// the markdown table and fails on drift). Item bonuses use the item catalog, rarity_tiers.csv and temper_levels.csv.
/// </summary>
public sealed class CharacterRules
{
    public const string Might = "might";
    public const string Finesse = "finesse";
    public const string Spirit = "spirit";
    public const string Vigor = "vigor";

    /// <summary>"2 wolne punkty" per level-up (CHARACTER_PROGRESSION §1.2).</summary>
    public const int StatPointsPerLevel = 2;

    /// <summary>"1 punkt za każdy awans od poziomu 2" (CLASSES_SKILLS §2).</summary>
    public const int SkillPointsPerLevel = 1;

    public const string AffixMaxHpFlat = "max_hp_flat";
    public const string AffixMaxMpFlat = "max_mp_flat";

    public const string RarityResourceName = "MetinAR.Server.Design.rarity_tiers.csv";
    public const string TemperResourceName = "MetinAR.Server.Design.temper_levels.csv";

    private static SavePrimaryStats Stats(int might, int finesse, int spirit, int vigor) =>
        new() { Might = might, Finesse = finesse, Spirit = spirit, Vigor = vigor };

    /// <summary>CHARACTER_PROGRESSION §1.2 class table (start Moc/Zwinność/Duch/Krzepa, auto +1, HP/MP per level).</summary>
    public static IReadOnlyList<ClassDefinition> Classes { get; } =
    [
        new("warrior", Stats(6, 3, 2, 5), Might, Vigor, 14, 4),
        new("ninja", Stats(3, 6, 2, 4), Finesse, Vigor, 9, 5),
        new("shaman", Stats(2, 3, 6, 4), Spirit, Vigor, 8, 9),
        new("slajter", Stats(5, 2, 5, 4), Might, Spirit, 11, 7),
    ];

    private readonly IReadOnlyDictionary<string, double> _rarityMultiplier;
    private readonly IReadOnlyDictionary<int, int> _temperBonusPct;

    public CharacterRules(ItemCatalog catalog, IReadOnlyDictionary<string, double> rarityMultiplier, IReadOnlyDictionary<int, int> temperBonusPct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rarityMultiplier);
        ArgumentNullException.ThrowIfNull(temperBonusPct);
        Catalog = catalog;
        _rarityMultiplier = rarityMultiplier;
        _temperBonusPct = temperBonusPct;
    }

    public ItemCatalog Catalog { get; }

    public static ClassDefinition Class(string classId) =>
        Classes.FirstOrDefault(c => c.Id == classId) ?? throw new ArgumentException($"Unknown class '{classId}'.", nameof(classId));

    /// <summary>Start + automatic class growth (+1 per level-up in two class stats) + allocated points.</summary>
    public static SavePrimaryStats Totals(string classId, int level, SavePrimaryStats allocated)
    {
        ArgumentNullException.ThrowIfNull(allocated);
        ClassDefinition c = Class(classId);
        int growth = Math.Max(0, level - 1);
        int Auto(string stat) => c.AutoStatA == stat || c.AutoStatB == stat ? growth : 0;
        return new SavePrimaryStats
        {
            Might = c.Start.Might + Auto(Might) + allocated.Might,
            Finesse = c.Start.Finesse + Auto(Finesse) + allocated.Finesse,
            Spirit = c.Start.Spirit + Auto(Spirit) + allocated.Spirit,
            Vigor = c.Start.Vigor + Auto(Vigor) + allocated.Vigor,
        };
    }

    /// <summary><c>MaxHp = 60 + L × hpPerLevel + Vigor × 6 + przedmioty</c> (CHARACTER_PROGRESSION §1.3).</summary>
    public int MaxHp(SaveGameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SaveCharacter ch = document.Character;
        SavePrimaryStats totals = Totals(ch.ClassId, ch.Level, document.Stats.Allocated);
        return checked(60 + (ch.Level * Class(ch.ClassId).HpPerLevel) + (totals.Vigor * 6) + ItemBonus(document, true));
    }

    /// <summary><c>MaxMp = 20 + L × mpPerLevel + Spirit × 5 + przedmioty</c> (CHARACTER_PROGRESSION §1.3).</summary>
    public int MaxMp(SaveGameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SaveCharacter ch = document.Character;
        SavePrimaryStats totals = Totals(ch.ClassId, ch.Level, document.Stats.Allocated);
        return checked(20 + (ch.Level * Class(ch.ClassId).MpPerLevel) + (totals.Spirit * 5) + ItemBonus(document, false));
    }

    /// <summary>Clamp current HP/MP to [0, max]. Never heals: a value below max is kept.</summary>
    public SaveGameDocument ClampVitals(SaveGameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int hp = Math.Clamp(document.Character.HpCurrent, 0, MaxHp(document));
        int mp = Math.Clamp(document.Character.MpCurrent, 0, MaxMp(document));
        return hp == document.Character.HpCurrent && mp == document.Character.MpCurrent
            ? document
            : document with { Character = document.Character with { HpCurrent = hp, MpCurrent = mp } };
    }

    /// <summary>
    /// "+ przedmioty" for equipped items: catalog <c>max_hp</c>/<c>max_mp</c> base stat × rarity multiplier ×
    /// (1 + temper%), plus rolled <c>max_hp_flat</c>/<c>max_mp_flat</c> affix values. The spec gives no rounding, so
    /// each item's bonus is rounded UP (the clamp must never cut below what a client could legitimately show).
    /// </summary>
    private int ItemBonus(SaveGameDocument document, bool hp)
    {
        int total = 0;
        foreach (SaveEquippedItem equipped in document.Equipment)
        {
            SaveItemInstance item = equipped.Item;
            ItemDefinition? definition = Catalog.Find(item.DefinitionId);
            int baseStat = definition is null ? 0 : hp ? definition.MaxHpBonus : definition.MaxMpBonus;
            double multiplier = _rarityMultiplier.GetValueOrDefault(item.Rarity, 1.0)
                * (1 + (_temperBonusPct.GetValueOrDefault(item.Temper, 0) / 100.0));
            string affixId = hp ? AffixMaxHpFlat : AffixMaxMpFlat;
            double affixes = item.Affixes.Where(a => a.AffixId == affixId).Sum(a => a.Value);
            total = checked(total + (int)Math.Ceiling((baseStat * multiplier) + affixes - 1e-9));
        }

        return total;
    }

    public static CharacterRules LoadEmbedded(ItemCatalog catalog)
    {
        static string Read(string resource)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return Parse(catalog, Read(RarityResourceName), Read(TemperResourceName));
    }

    public static CharacterRules Parse(ItemCatalog catalog, string rarityCsv, string temperCsv)
    {
        var rarity = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string[] row in Rows(rarityCsv, "rarityId", "baseStatMultiplier"))
        {
            rarity[row[0]] = double.Parse(row[1], CultureInfo.InvariantCulture);
        }

        var temper = new Dictionary<int, int>();
        foreach (string[] row in Rows(temperCsv, "temperLevel", "baseStatBonusPct"))
        {
            temper[int.Parse(row[0], CultureInfo.InvariantCulture)] = int.Parse(row[1], CultureInfo.InvariantCulture);
        }

        foreach (string id in SaveGameRules.Rarities)
        {
            if (!rarity.ContainsKey(id))
            {
                throw new FormatException($"rarity_tiers.csv has no row for '{id}'.");
            }
        }

        return new CharacterRules(catalog, rarity, temper);
    }

    private static IEnumerable<string[]> Rows(string csv, string keyColumn, string valueColumn)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] header = lines[0].Split(',');
        int key = Array.IndexOf(header, keyColumn);
        int value = Array.IndexOf(header, valueColumn);
        if (key < 0 || value < 0)
        {
            throw new FormatException($"Table needs '{keyColumn}' and '{valueColumn}' columns.");
        }

        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            yield return [cells[key], cells[value]];
        }
    }
}
