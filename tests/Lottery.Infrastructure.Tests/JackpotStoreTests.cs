using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

public sealed class JackpotStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly JackpotStoreRepository _store;

    public JackpotStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lottery-jackpot-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";
        var factory = new SqliteConnectionFactory(connectionString);
        new DatabaseInitializer(factory, connectionString).Initialize();
        _store = new JackpotStoreRepository(factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Save_ThenGet_RoundTrips()
    {
        var estimate = new JackpotEstimate(Game.MegaMillions, 800_000_000m, 344_200_000m,
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        await _store.SaveAsync(estimate, CancellationToken.None);
        var loaded = await _store.GetAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Equal(estimate, loaded);
        Assert.Null(await _store.GetAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task Save_Twice_UpsertsLatest()
    {
        var t = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(new JackpotEstimate(Game.MegaMillions, 743_000_000m, 323_400_000m, t), CancellationToken.None);
        await _store.SaveAsync(new JackpotEstimate(Game.MegaMillions, 800_000_000m, 344_200_000m, t.AddHours(1)), CancellationToken.None);

        var loaded = await _store.GetAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Equal(800_000_000m, loaded!.NextEstimatedJackpot);
        Assert.Equal(t.AddHours(1), loaded.UpdatedAtUtc);
    }
}
