using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The ledger is the only guard against re-running the 4,500-row seed on every
/// boot, so its round-trip fidelity - including the timestamp's offset - is
/// load-bearing. Real SQLite, same as the other repository tests.
/// </summary>
public sealed class ImportLedgerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ImportLedgerRepository _ledger;

    public ImportLedgerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lottery-ledger-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";
        var factory = new SqliteConnectionFactory(connectionString);
        new DatabaseInitializer(factory, connectionString).Initialize();
        _ledger = new ImportLedgerRepository(factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Record_ThenGet_RoundTripsEveryField()
    {
        // Non-UTC offset on purpose: the "O" format must preserve it exactly.
        var record = new ImportRecord(Game.Powerball, "snapshot:data.ny.gov",
            new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.FromHours(2)),
            1971, new DateOnly(2010, 2, 3), new DateOnly(2026, 7, 25));

        await _ledger.RecordAsync(record, CancellationToken.None);
        var loaded = await _ledger.GetAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(record, loaded);
        Assert.Equal(TimeSpan.FromHours(2), loaded!.CompletedAtUtc.Offset);
    }

    [Fact]
    public async Task Get_UnrecordedGame_ReturnsNull()
    {
        Assert.Null(await _ledger.GetAsync(Game.MegaMillions, CancellationToken.None));
    }

    [Fact]
    public async Task Records_AreIsolatedPerGame()
    {
        var powerball = new ImportRecord(Game.Powerball, "snapshot:data.ny.gov",
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero),
            1971, new DateOnly(2010, 2, 3), new DateOnly(2026, 7, 25));
        var megaMillions = new ImportRecord(Game.MegaMillions, "snapshot:data.ny.gov",
            new DateTimeOffset(2026, 7, 27, 12, 5, 0, TimeSpan.Zero),
            2522, new DateOnly(2002, 5, 17), new DateOnly(2026, 7, 24));

        await _ledger.RecordAsync(powerball, CancellationToken.None);
        await _ledger.RecordAsync(megaMillions, CancellationToken.None);

        Assert.Equal(powerball, await _ledger.GetAsync(Game.Powerball, CancellationToken.None));
        Assert.Equal(megaMillions, await _ledger.GetAsync(Game.MegaMillions, CancellationToken.None));
    }
}
