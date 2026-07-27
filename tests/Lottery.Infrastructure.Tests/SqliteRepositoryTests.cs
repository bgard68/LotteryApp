using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Repository tests against a real SQLite database (temp file, migrated by DbUp) -
/// with Dapper the SQL is the logic, so mocking the connection would test nothing.
/// </summary>
public sealed class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DrawRepository _repo;

    public SqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lottery-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";
        _factory = new SqliteConnectionFactory(connectionString);
        new DatabaseInitializer(_factory, connectionString).Initialize();
        _repo = new DrawRepository(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static readonly Draw Saturday = Draw.Create(
        Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18, 389_000_000m, false);

    [Fact]
    public async Task Upsert_ThenGetLatest_RoundTrips()
    {
        var inserted = await _repo.UpsertAsync(Saturday, CancellationToken.None);
        var latest = await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None);

        Assert.True(inserted);
        Assert.Equal(Saturday, latest);
    }

    [Fact]
    public async Task Upsert_SameDrawTwice_IsIdempotent()
    {
        await _repo.UpsertAsync(Saturday, CancellationToken.None);
        var second = await _repo.UpsertAsync(Saturday, CancellationToken.None);

        Assert.False(second);
        Assert.Equal(1, await _repo.CountAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task Games_AreFullyIsolated()
    {
        await _repo.UpsertAsync(Saturday, CancellationToken.None);
        var mm = Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 24), [3, 22, 38, 45, 67], 12);
        await _repo.UpsertAsync(mm, CancellationToken.None);

        Assert.Equal(1, await _repo.CountAsync(Game.Powerball, CancellationToken.None));
        var latestMm = await _repo.GetLatestAsync(Game.MegaMillions, CancellationToken.None);
        Assert.Equal(mm, latestMm);
    }

    [Fact]
    public async Task FindMatches_ComputesSetWiseInSql()
    {
        await _repo.BulkInsertAsync([
            Saturday,
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [1, 2, 3, 4, 5], 6),
        ], CancellationToken.None);

        // 3 whites + special vs Saturday; nothing vs the other draw.
        var rows = await _repo.FindMatchesAsync(Game.Powerball, [7, 19, 33, 60, 61], 18, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 7, 25), row.DrawDate);
        Assert.Equal(3, row.WhiteMatches);
        Assert.True(row.SpecialMatched);
        Assert.Equal([7, 19, 33, 51, 64], row.DrawnWhiteBalls);
        Assert.Equal(18, row.DrawnSpecial);
    }

    [Fact]
    public async Task GetRange_FiltersAndLimits()
    {
        await _repo.BulkInsertAsync([
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 18), [1, 2, 3, 4, 5], 6),
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 20), [6, 7, 8, 9, 10], 7),
            Saturday,
        ], CancellationToken.None);

        var range = await _repo.GetRangeAsync(Game.Powerball,
            new DateOnly(2026, 7, 19), new DateOnly(2026, 7, 26), 1, CancellationToken.None);

        var only = Assert.Single(range); // newest first, limited to 1
        Assert.Equal(new DateOnly(2026, 7, 25), only.DrawDate);
    }
}
