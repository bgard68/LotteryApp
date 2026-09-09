using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// SQL paths the base repository suite leaves uncovered: the null-bound halves
/// of the range query's "(@from IS NULL OR ...)" predicates, and the match
/// query's inclusion rule (special alone comes back with zero whites; two
/// whites without the special is filtered out server-side; games never cross)
/// - with Dapper the SQL is the logic.
/// </summary>
public sealed class DrawRepositoryEdgeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DrawRepository _repo;

    public DrawRepositoryEdgeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lottery-repo-edge-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";
        var factory = new SqliteConnectionFactory(connectionString);
        new DatabaseInitializer(factory, connectionString).Initialize();
        _repo = new DrawRepository(factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static readonly Draw July18 = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 18), [10, 20, 30, 40, 50], 9);
    private static readonly Draw July22 = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [4, 5, 22, 50, 58], 1);
    private static readonly Draw July25 = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [3, 4, 24, 36, 47], 17);

    private Task SeedThreeDrawsAsync() =>
        _repo.BulkInsertAsync([July18, July22, July25], CancellationToken.None);

    [Fact]
    public async Task GetRange_FromOnly_ReturnsEverythingSinceThatDate()
    {
        await SeedThreeDrawsAsync();

        var range = await _repo.GetRangeAsync(Game.Powerball, new DateOnly(2026, 7, 21), null, 50, CancellationToken.None);

        Assert.Equal(2, range.Count);
        Assert.Equal(new DateOnly(2026, 7, 25), range[0].DrawDate); // newest first
        Assert.Equal(new DateOnly(2026, 7, 22), range[1].DrawDate);
    }

    [Fact]
    public async Task GetRange_ToOnly_ReturnsEverythingUpToThatDate()
    {
        await SeedThreeDrawsAsync();

        var range = await _repo.GetRangeAsync(Game.Powerball, null, new DateOnly(2026, 7, 21), 50, CancellationToken.None);

        var only = Assert.Single(range);
        Assert.Equal(new DateOnly(2026, 7, 18), only.DrawDate);
    }

    [Fact]
    public async Task FindMatches_SpecialAlone_IsIncludedWithZeroWhites()
    {
        await SeedThreeDrawsAsync();

        // No white overlap with July25 ([3,4,24,36,47]); special 17 hits it.
        var rows = await _repo.FindMatchesAsync(Game.Powerball, [11, 12, 13, 14, 15], 17, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 7, 25), row.DrawDate);
        Assert.Equal(0, row.WhiteMatches);
        Assert.True(row.SpecialMatched);
    }

    [Fact]
    public async Task FindMatches_TwoWhitesWithoutSpecial_IsFilteredOutInSql()
    {
        await SeedThreeDrawsAsync();

        // Exactly 2 whites of July25 ([3,4]), wrong special: below every tier,
        // so the WHERE clause must exclude it server-side.
        var rows = await _repo.FindMatchesAsync(Game.Powerball, [3, 4, 60, 61, 62], 26, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FindMatches_NeverCrossesGames()
    {
        await SeedThreeDrawsAsync();
        // Same numbers as July25 but stored under the other game.
        await _repo.UpsertAsync(Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 24), [3, 4, 24, 36, 47], 17), CancellationToken.None);

        var rows = await _repo.FindMatchesAsync(Game.Powerball, [3, 4, 24, 36, 47], 17, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 7, 25), row.DrawDate);
        Assert.Equal(5, row.WhiteMatches);
    }
}
