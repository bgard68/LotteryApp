using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Lottery.Infrastructure.Persistence;

public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly SqlRetryLogicBaseProvider _retry;

    public SqlServerConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;

        // Azure SQL serverless auto-pauses when idle; the first connection after a
        // pause times out while the database resumes. Retry transient errors with
        // long backoff so the wake-up succeeds instead of surfacing a 500.
        _retry = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 8,
            DeltaTime = TimeSpan.FromSeconds(2),
            MaxTimeInterval = TimeSpan.FromSeconds(15),
            TransientErrors = [-2, 40613, 40197, 40501, 49918, 49919, 49920],
        });
    }

    public SqlDialect Dialect => SqlDialect.SqlServer;

    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString) { RetryLogicProvider = _retry };
        await conn.OpenAsync(ct);
        return conn;
    }
}
