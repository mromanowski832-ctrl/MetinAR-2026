using System.Globalization;
using System.Reflection;

namespace MetinAR.Server.SaveGames;

/// <summary>One row of the item table: what the SaveGame rules need (stack limit, category, flat max HP/MP bonus).</summary>
public sealed record ItemDefinition(string Id, string Category, int StackLimit, int MaxHpBonus, int MaxMpBonus)
{
    public bool IsStackable => StackLimit > 1;
}

/// <summary>
/// The item catalog, read from the design tables in <c>docs/design/tables/</c> (embedded at build time, see
/// <see cref="Parse"/>). Stack limits come from their <c>stack</c> column (ITEMS_COMBAT_SPEC §2: consumables 50,
/// materials 99, Olej Kowalski 20, skill-tier items 20/10/5); <c>baseStats</c> keys <c>max_hp</c>/<c>max_mp</c> feed
/// the max HP/MP clamp.
/// Unknown definition ids are rejected. Drop tables and server-computed rewards are #56.
/// </summary>
public sealed class ItemCatalog
{
    public const string ResourceName = "MetinAR.Server.Design.items_starter.csv";
    public const string ShopResourceName = "MetinAR.Server.Design.shop_starter_merchant.csv";
    public const string SkillTierItemsResourceName = "MetinAR.Server.Design.skill_tier_items.csv";
    public const string CategoryMaterial = "material";
    public const string CategoryEquipment = "equipment";
    public const string CategorySkillTier = "skill_tier";

    private readonly Dictionary<string, ItemDefinition> _items;

    public ItemCatalog(IEnumerable<ItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (ItemDefinition item in items)
        {
            if (item.StackLimit < 1 || item.StackLimit > SaveGameRules.MaxStackQuantity)
            {
                throw new ArgumentException($"Item '{item.Id}' has an invalid stack limit.", nameof(items));
            }

            if (!_items.TryAdd(item.Id, item))
            {
                throw new ArgumentException($"Duplicate item id '{item.Id}'.", nameof(items));
            }
        }
    }

    public IReadOnlyCollection<ItemDefinition> Items => _items.Values;

    public ItemDefinition? Find(string? id) => id is not null && _items.TryGetValue(id, out ItemDefinition? item) ? item : null;

    public static ItemCatalog LoadEmbedded()
    {
        static string Read(string resource)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return Parse(Read(ResourceName), Read(ShopResourceName), Read(SkillTierItemsResourceName), SaveGameRules.ClassIds);
    }

    /// <summary>
    /// Builds the catalog from the three design tables that define item ids: <c>items_starter.csv</c> (primary, has
    /// <c>baseStats</c>), <c>shop_starter_merchant.csv</c> (adds shop-only consumables/materials; <c>service</c> rows
    /// such as the bag expansion are not items) and <c>skill_tier_items.csv</c> (<c>&lt;classId&gt;</c> expands per class).
    /// An id that appears in several tables must have the same stack limit everywhere.
    /// </summary>
    public static ItemCatalog Parse(string itemsCsv, string shopCsv, string skillTierItemsCsv, IEnumerable<string> classIds)
    {
        ArgumentNullException.ThrowIfNull(classIds);
        var items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        void Add(ItemDefinition item, string table)
        {
            if (items.TryGetValue(item.Id, out ItemDefinition? existing))
            {
                if (existing.StackLimit != item.StackLimit)
                {
                    throw new FormatException($"Item '{item.Id}' has stack {existing.StackLimit} in one table and {item.StackLimit} in {table}.");
                }

                return;
            }

            items.Add(item.Id, item);
        }

        foreach (Row row in Rows(itemsCsv, "items_starter.csv"))
        {
            (int maxHp, int maxMp) = BaseStats(row["baseStats"]);
            Add(new ItemDefinition(row["itemId"], row["category"], row.Int("stack"), maxHp, maxMp), "items_starter.csv");
        }

        foreach (Row row in Rows(shopCsv, "shop_starter_merchant.csv"))
        {
            if (row["category"] != "service")
            {
                Add(new ItemDefinition(row["itemId"], row["category"], row.Int("stack"), 0, 0), "shop_starter_merchant.csv");
            }
        }

        foreach (Row row in Rows(skillTierItemsCsv, "skill_tier_items.csv"))
        {
            string id = row["itemId"];
            IEnumerable<string> ids = id.Contains("<classId>", StringComparison.Ordinal)
                ? classIds.Select(c => id.Replace("<classId>", c, StringComparison.Ordinal))
                : [id];
            foreach (string expanded in ids)
            {
                Add(new ItemDefinition(expanded, CategorySkillTier, row.Int("stack"), 0, 0), "skill_tier_items.csv");
            }
        }

        return new ItemCatalog(items.Values);
    }

    private static (int MaxHp, int MaxMp) BaseStats(string cell)
    {
        int maxHp = 0;
        int maxMp = 0;
        foreach (string stat in cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = stat.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "max_hp")
            {
                maxHp = int.Parse(pair[1], CultureInfo.InvariantCulture);
            }
            else if (pair.Length == 2 && pair[0] == "max_mp")
            {
                maxMp = int.Parse(pair[1], CultureInfo.InvariantCulture);
            }
        }

        return (maxHp, maxMp);
    }

    private static IEnumerable<Row> Rows(string csv, string table)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] header = lines[0].Split(',');
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            if (cells.Length != header.Length)
            {
                throw new FormatException($"{table} row has {cells.Length} cells, expected {header.Length}: '{line}'.");
            }

            yield return new Row(table, header, cells);
        }
    }

    private sealed class Row(string table, string[] header, string[] cells)
    {
        public string this[string column] => Array.IndexOf(header, column) is int i and >= 0
            ? cells[i]
            : throw new FormatException($"{table} needs a '{column}' column.");

        public int Int(string column) => int.Parse(this[column], CultureInfo.InvariantCulture);
    }
}
