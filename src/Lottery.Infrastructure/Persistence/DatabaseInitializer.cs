using DbUp;
using DbUp.Engine;
using Lottery.Application.Abstractions;

namespace Lottery.Infrastructure.Persistence;

/// <summary>
/// DbUp migrations from embedded, provider-matched scripts. Both script folders
/// share the same numbering; a CI check keeps them in lockstep.
/// </summary>
public sealed class DatabaseInitializer(IDbConnectionFactory factory, string connectionString) : IDatabaseInitializer
{
    public void Initialize()
    {
        var builder = factory.Dialect switch
        {
            SqlDialect.Sqlite => DeployChanges.To.SqliteDatabase(connectionString),
            SqlDialect.SqlServer => DeployChanges.To.SqlDatabase(connectionString),
            _ => throw new InvalidOperationException($"Unknown dialect {factory.Dialect}."),
        };

        var folder = factory.Dialect == SqlDialect.Sqlite ? "Sqlite" : "SqlServer";
        var result = builder
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseInitializer).Assembly,
                name => name.Contains($".Migrations.{folder}.", StringComparison.Ordinal))
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException("Database migration failed.", result.Error);
    }
}
