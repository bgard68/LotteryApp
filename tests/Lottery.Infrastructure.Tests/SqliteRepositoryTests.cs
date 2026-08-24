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
    public async Task GetLatest_OnAnEmptyDatabase_IsNull()
    {
        // First boot, before seeding: RefreshGame branches on this to decide
        // whether it is doing gap-repair or a full import.
        Assert.Null(await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task GetRange_WithNoBounds_ReturnsEverythingNewestFirst()
    {
        // The default listing: both bounds omitted, only the limit applies.
        await _repo.BulkInsertAsync([
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 18), [1, 2, 3, 4, 5], 6),
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 20), [6, 7, 8, 9, 10], 7),
            Saturday,
        ], CancellationToken.None);

        var range = await _repo.GetRangeAsync(Game.Powerball, null, null, 10, CancellationToken.None);

        Assert.Equal(
            [new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 18)],
            range.Select(d => d.DrawDate));
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

    // Jackpot figures arrive after the numbers do: the winning numbers land from
    // Socrata first, and the jackpot feed backfills the amount and the won/rolled
    // flag onto the already-stored draw.

    [Fact]
    public async Task UpdateJackpot_BackfillsAmountAndWonFlag()
    {
        var withoutJackpot = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);
        await _repo.UpsertAsync(withoutJackpot, CancellationToken.None);

        await _repo.UpdateJackpotAsync(Game.Powerball, new DateOnly(2026, 7, 25),
            389_000_000m, true, CancellationToken.None);

        var latest = await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(389_000_000m, latest!.JackpotAmount);
        Assert.True(latest.JackpotWon);
        // The numbers themselves are untouched.
        Assert.Equal([7, 19, 33, 51, 64], latest.WhiteBalls);
        Assert.Equal(18, latest.Special);
    }

    [Fact]
    public async Task UpdateJackpot_OverwritesAnEarlierEstimate()
    {
        await _repo.UpsertAsync(Saturday, CancellationToken.None); // stored at 389M, not won

        await _repo.UpdateJackpotAsync(Game.Powerball, Saturday.DrawDate,
            412_500_000m, true, CancellationToken.None);

        var latest = await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(412_500_000m, latest!.JackpotAmount);
        Assert.True(latest.JackpotWon);
    }

    [Fact]
    public async Task UpdateJackpot_WithNulls_ClearsTheFigures()
    {
        await _repo.UpsertAsync(Saturday, CancellationToken.None);

        await _repo.UpdateJackpotAsync(Game.Powerball, Saturday.DrawDate, null, null, CancellationToken.None);

        var latest = await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None);
        Assert.Null(latest!.JackpotAmount);
        Assert.Null(latest.JackpotWon);
    }

    [Fact]
    public async Task UpdateJackpot_ForADrawThatIsNotStored_DoesNothing()
    {
        // The jackpot feed reports a last-draw date the numbers feed has not
        // delivered yet. That must be a no-op, not an error and not a phantom row.
        await _repo.UpsertAsync(Saturday, CancellationToken.None);

        await _repo.UpdateJackpotAsync(Game.Powerball, new DateOnly(2026, 7, 29),
            999_000_000m, true, CancellationToken.None);

        Assert.Equal(1, await _repo.CountAsync(Game.Powerball, CancellationToken.None));
        Assert.Equal(Saturday, await _repo.GetLatestAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateJackpot_TouchesOnlyTheMatchingGame()
    {
        // Both games can draw on the same date, so Game has to be in the WHERE clause.
        var sameDayMegaMillions = Draw.Create(
            Game.MegaMillions, Saturday.DrawDate, [3, 22, 38, 45, 67], 12, 200_000_000m, false);
        await _repo.UpsertAsync(Saturday, CancellationToken.None);
        await _repo.UpsertAsync(sameDayMegaMillions, CancellationToken.None);

        await _repo.UpdateJackpotAsync(Game.Powerball, Saturday.DrawDate,
            412_500_000m, true, CancellationToken.None);

        Assert.Equal(sameDayMegaMillions, await _repo.GetLatestAsync(Game.MegaMillions, CancellationToken.None));
    }

    [Fact]
    public async Task EarliestDrawDate_IsNullWhenNothingIsStored()
    {
        Assert.Null(await _repo.EarliestDrawDateAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task EarliestDrawDate_IsTheOldestStoredDraw_PerGame()
    {
        await _repo.BulkInsertAsync([
            Saturday,
            Draw.Create(Game.Powerball, new DateOnly(2010, 2, 3), [1, 2, 3, 4, 5], 6),
            Draw.Create(Game.Powerball, new DateOnly(2018, 6, 9), [6, 7, 8, 9, 10], 7),
            Draw.Create(Game.MegaMillions, new DateOnly(2002, 5, 17), [11, 12, 13, 14, 15], 8),
        ], CancellationToken.None);

        Assert.Equal(new DateOnly(2010, 2, 3),
            await _repo.EarliestDrawDateAsync(Game.Powerball, CancellationToken.None));
        Assert.Equal(new DateOnly(2002, 5, 17),
            await _repo.EarliestDrawDateAsync(Game.MegaMillions, CancellationToken.None));
    }
}
