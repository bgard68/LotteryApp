using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Lottery.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public SqlDialect Dialect => SqlDialect.Sqlite;

    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
