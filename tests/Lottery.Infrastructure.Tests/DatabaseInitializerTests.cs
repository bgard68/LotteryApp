using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Every boot runs migrations before serving, so re-running against an
/// already-migrated database must be a safe no-op that preserves data.
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lottery-dbup-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Initialize_Twice_IsIdempotentAndKeepsData()
    {
        var connectionString = $"Data Source={_dbPath}";
        var factory = new SqliteConnectionFactory(connectionString);
        var initializer = new DatabaseInitializer(factory, connectionString);
        initializer.Initialize();
        var repo = new DrawRepository(factory);
        await repo.UpsertAsync(
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [3, 4, 24, 36, 47], 17), CancellationToken.None);

        initializer.Initialize(); // second boot

        Assert.Equal(1, await repo.CountAsync(Game.Powerball, CancellationToken.None));
        var latest = await repo.GetLatestAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 7, 25), latest!.DrawDate);
    }
}
