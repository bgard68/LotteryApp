using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The ledger is what stops the one-time history import from running twice, so
/// the round trip has to be exact: every field goes through the database as a
/// string and comes back parsed. Real SQLite (temp file, migrated by DbUp) -
/// with Dapper the SQL is the logic.
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

    private static readonly ImportRecord PowerballImport = new(
        Game.Powerball,
        "snapshot:data.ny.gov",
        new DateTimeOffset(2026, 7, 27, 12, 34, 56, 789, TimeSpan.Zero),
        1978,
        new DateOnly(2010, 2, 3),
        new DateOnly(2026, 7, 25));

    [Fact]
    public async Task Record_ThenGet_RoundTripsEveryField()
    {
        await _ledger.RecordAsync(PowerballImport, CancellationToken.None);

        var loaded = await _ledger.GetAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(PowerballImport, loaded);
    }

    [Fact]
    public async Task CompletedAt_KeepsSubSecondPrecision()
    {
        // Stored round-trip format ("O"); a coarser format would make repeated
        // imports on the same day indistinguishable.
        await _ledger.RecordAsync(PowerballImport, CancellationToken.None);

        var loaded = await _ledger.GetAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(PowerballImport.CompletedAtUtc, loaded!.CompletedAtUtc);
        Assert.Equal(789, loaded.CompletedAtUtc.Millisecond);
    }

    [Fact]
    public async Task Get_ForAGameNeverImported_IsNull()
    {
        Assert.Null(await _ledger.GetAsync(Game.Powerball, CancellationToken.None));

        await _ledger.RecordAsync(PowerballImport, CancellationToken.None);

        // One game's import must never look like the other's.
        Assert.Null(await _ledger.GetAsync(Game.MegaMillions, CancellationToken.None));
    }

    [Fact]
    public async Task EachGame_KeepsItsOwnRecord()
    {
        var megaMillions = PowerballImport with
        {
            Game = Game.MegaMillions,
            Source = "socrata:5xaw-6ayf",
            DrawCount = 2431,
        };

        await _ledger.RecordAsync(PowerballImport, CancellationToken.None);
        await _ledger.RecordAsync(megaMillions, CancellationToken.None);

        Assert.Equal(PowerballImport, await _ledger.GetAsync(Game.Powerball, CancellationToken.None));
        Assert.Equal(megaMillions, await _ledger.GetAsync(Game.MegaMillions, CancellationToken.None));
    }

    [Fact]
    public async Task RecordingAGameTwice_IsRejectedByThePrimaryKey()
    {
        // Game is the primary key: the ledger holds one row per game, and a
        // second import attempt must fail loudly rather than append a duplicate
        // that would make "have we imported this?" ambiguous.
        await _ledger.RecordAsync(PowerballImport, CancellationToken.None);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => _ledger.RecordAsync(PowerballImport with { DrawCount = 9999 }, CancellationToken.None));

        var loaded = await _ledger.GetAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(1978, loaded!.DrawCount);
    }
}
