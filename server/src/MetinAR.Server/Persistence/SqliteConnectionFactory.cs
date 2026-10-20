using Microsoft.Data.Sqlite;

namespace MetinAR.Server.Persistence;

public sealed class SqliteConnectionFactory
{
    public SqliteConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 30,
        };
        ConnectionString = builder.ToString();
        DataSource = builder.DataSource;
    }

    public string ConnectionString { get; }

    public string DataSource { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
