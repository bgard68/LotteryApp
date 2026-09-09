using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Startup migrations. This runs on every boot, so the two things that matter
/// are that a second run is a no-op over an already-migrated database, and that
/// a failed migration stops the process instead of leaving the app serving
/// queries against a half-built schema.
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"lottery-init-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task RunningTwice_IsANoOp_AndKeepsTheData()
    {
        // Every boot calls Initialize(); the DbUp journal is what keeps the
        // second one from re-running 0001 and wiping the tables.
        var connectionString = $"Data Source={_dbPath}";
        var factory = new SqliteConnectionFactory(connectionString);
        var initializer = new DatabaseInitializer(factory, connectionString);

        initializer.Initialize();
        var repo = new DrawRepository(factory);
        await repo.UpsertAsync(
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18),
            CancellationToken.None);

        initializer.Initialize();

        Assert.Equal(1, await repo.CountAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public void AnUnknownDialect_FailsLoudly()
    {
        // Adding a third provider without adding its script folder must not
        // silently fall through to the SQLite scripts.
        var initializer = new DatabaseInitializer(new UnknownDialectConnectionFactory(), "Data Source=:memory:");

        var ex = Assert.Throws<InvalidOperationException>(initializer.Initialize);
        Assert.Contains("Unknown dialect", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedMigration_ThrowsWithTheUnderlyingCause()
    {
        // Nothing is listening on port 1, so the SQL Server upgrade cannot run.
        // The point is the wrapping: DbUp reports failure through its result
        // object, and the initializer has to turn that into a throw - otherwise
        // startup continues against a database with no tables.
        const string unreachable =
            "Server=127.0.0.1,1;Database=lottery;Connect Timeout=1;Encrypt=False;TrustServerCertificate=True";
        var initializer = new DatabaseInitializer(new SqlServerConnectionFactory(unreachable), unreachable);

        var ex = Assert.Throws<InvalidOperationException>(initializer.Initialize);
        Assert.Contains("Database migration failed", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }
}
