using System.Reflection;
using Microsoft.Data.Sqlite;

namespace MetinAR.Server.Persistence;

/// <summary>
/// Applies the ordered SQL scripts embedded from <c>Persistence/Migrations/*.sql</c> exactly once each,
/// recording them in <c>schema_migrations</c>. Each script runs inside its own transaction.
/// </summary>
public sealed partial class SchemaMigrator
{
    private const string ResourceMarker = ".Persistence.Migrations.";

    private readonly SqliteConnectionFactory _connections;
    private readonly TimeProvider _time;
    private readonly ILogger<SchemaMigrator> _logger;

    public SchemaMigrator(SqliteConnectionFactory connections, TimeProvider time, ILogger<SchemaMigrator> logger)
    {
        _connections = connections;
        _time = time;
        _logger = logger;
    }

    /// <summary>Names of all migrations shipped with this build, in apply order.</summary>
    public static IReadOnlyList<string> KnownMigrations { get; } = LoadScripts().Select(s => s.Id).ToArray();

    /// <returns>The migration ids applied by this call (empty when the schema was already current).</returns>
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        EnsureDataDirectory(_connections.DataSource);

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            "CREATE TABLE IF NOT EXISTS schema_migrations (id TEXT NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);",
            cancellationToken).ConfigureAwait(false);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT id FROM schema_migrations;";
            await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0));
            }
        }

        var newlyApplied = new List<string>();
        foreach ((string id, string sql) in LoadScripts())
        {
            if (applied.Contains(id))
            {
                continue;
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
            await using (SqliteCommand record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_migrations (id, applied_at) VALUES ($id, $at);";
                record.Parameters.AddWithValue("$id", id);
                record.Parameters.AddWithValue("$at", UtcTimestamp.ToText(_time.GetUtcNow()));
                await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            newlyApplied.Add(id);
            LogApplied(_logger, id);
        }

        return newlyApplied;
    }

    private static void EnsureDataDirectory(string dataSource)
    {
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<(string Id, string Sql)> LoadScripts()
    {
        Assembly assembly = typeof(SchemaMigrator).Assembly;
        var scripts = new List<(string Id, string Sql)>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(ResourceMarker, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal)))
        {
            string id = name[(name.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..^".sql".Length];
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded migration {name}.");
            using var reader = new StreamReader(stream);
            scripts.Add((id, reader.ReadToEnd()));
        }

        scripts.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return scripts;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Applied schema migration {MigrationId}")]
    private static partial void LogApplied(ILogger logger, string migrationId);
}
