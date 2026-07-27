using System.Data.Common;

namespace Lottery.Infrastructure.Persistence;

/// <summary>
/// Connectionless access: every operation borrows a short-lived connection
/// (pooling makes this near-free) and disposes it immediately. No class in
/// the system ever holds an open connection.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken ct);

    /// <summary>Dialect-divergent SQL fragments; everything else stays portable.</summary>
    SqlDialect Dialect { get; }
}

public enum SqlDialect
{
    Sqlite,
    SqlServer,
}
