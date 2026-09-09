using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The SQL Server side of the provider split, as far as it can be exercised
/// without a live Azure SQL instance: the factory's dialect, and the SQL text
/// rewriting that dialect drives. Actually opening a connection needs a server,
/// so <see cref="SqlServerConnectionFactory.OpenAsync"/> is out of reach here.
/// </summary>
public class SqlServerDialectTests
{
    private const string ConnectionString =
        "Server=tcp:example.database.windows.net,1433;Database=lottery;Encrypt=True";

    [Fact]
    public void Factory_ReportsTheSqlServerDialect_WithoutConnecting()
    {
        // Construction happens during DI registration, long before any request:
        // it must not touch the network, however unreachable the server is.
        var factory = new SqlServerConnectionFactory(ConnectionString);

        Assert.Equal(SqlDialect.SqlServer, factory.Dialect);
    }

    [Theory]
    [InlineData("Server=tcp:x.database.windows.net,1433;Database=lottery;Encrypt=True")]
    [InlineData("Server=(local);Database=lottery;Integrated Security=True")]
    public void Factory_AcceptsAnyConnectionString(string connectionString)
    {
        // The connection string is not parsed or validated here - a bad one has
        // to surface at connect time, with SqlClient's own message.
        Assert.Equal(SqlDialect.SqlServer, new SqlServerConnectionFactory(connectionString).Dialect);
    }

    // The repository writes SQLite's LIMIT and rewrites it for SQL Server, which
    // has no LIMIT clause. These are the exact two fragments it emits; getting
    // the rewrite wrong is a runtime syntax error only Azure would ever see.

    [Fact]
    public void SqlServer_GetsOffsetFetchInsteadOfLimit()
    {
        Assert.Equal(
            "SELECT Game FROM Draws ORDER BY DrawDate DESC OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
            "SELECT Game FROM Draws ORDER BY DrawDate DESC LIMIT 1".AdaptLimit(SqlDialect.SqlServer));

        Assert.Equal(
            "SELECT Game FROM Draws ORDER BY DrawDate DESC OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            "SELECT Game FROM Draws ORDER BY DrawDate DESC LIMIT @limit".AdaptLimit(SqlDialect.SqlServer));
    }

    [Theory]
    [InlineData("SELECT Game FROM Draws ORDER BY DrawDate DESC LIMIT 1")]
    [InlineData("SELECT Game FROM Draws ORDER BY DrawDate DESC LIMIT @limit")]
    [InlineData("SELECT COUNT(*) FROM Draws WHERE Game = @game")]
    public void Sqlite_KeepsTheSqlExactlyAsWritten(string sql)
    {
        Assert.Same(sql, sql.AdaptLimit(SqlDialect.Sqlite));
    }
}
