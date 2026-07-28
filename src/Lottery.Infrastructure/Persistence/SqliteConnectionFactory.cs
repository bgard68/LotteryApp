using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Lottery.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
        EnsureDirectoryExists(connectionString);
    }

    public SqlDialect Dialect => SqlDialect.Sqlite;

    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>
    /// SQLite creates database FILES but never DIRECTORIES: pointing at
    /// `/home/data/lottery.db` on a fresh host throws "unable to open database
    /// file" forever. In Azure that produced a restart loop that burned the
    /// F1 plan's entire daily CPU quota (see lesson 25), so the directory is
    /// created once here, at construction, before anything tries to connect.
    /// </summary>
    internal static void EnsureDirectoryExists(string connectionString)
    {
        string dataSource;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            return; // malformed: let the connection attempt report it properly
        }

        // In-memory databases have no directory to create.
        if (string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory); // no-op when it already exists
    }
}
