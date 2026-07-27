using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

public class ImportHistoryTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero));

    private static readonly IReadOnlyList<Draw> History =
    [
        Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [1, 2, 3, 4, 5], 6),
        Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18),
    ];

    [Fact]
    public async Task Import_RunsOnce_ThenSkipsForever()
    {
        var repo = new FakeDrawRepository();
        var ledger = new FakeImportLedger();
        var source = new FakeHistorySource(History);
        var importer = new ImportHistory(repo, ledger, source, Time);

        var first = await importer.ExecuteAsync(Game.Powerball, CancellationToken.None);
        var second = await importer.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.False(first.Skipped);
        Assert.Equal(2, first.DrawCount);
        Assert.True(second.Skipped);
        Assert.Equal(1, source.Calls);
        Assert.Equal(2, repo.Draws.Count);
    }

    [Fact]
    public async Task Import_WithEraViolation_ThrowsAndWritesNothing()
    {
        var repo = new FakeDrawRepository();
        var ledger = new FakeImportLedger();
        var bad = new FakeHistorySource([
            Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 70], 18), // 70 > era max 69
        ]);
        var importer = new ImportHistory(repo, ledger, bad, Time);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ExecuteAsync(Game.Powerball, CancellationToken.None));

        Assert.Empty(repo.Draws);
        Assert.Null(await ledger.GetAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task Import_EmptySource_Throws()
    {
        var importer = new ImportHistory(new FakeDrawRepository(), new FakeImportLedger(),
            new FakeHistorySource([]), Time);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ExecuteAsync(Game.Powerball, CancellationToken.None));
    }
}
