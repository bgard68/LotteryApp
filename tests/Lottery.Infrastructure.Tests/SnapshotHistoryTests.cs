using Lottery.Domain;
using Lottery.Infrastructure.Seeding;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Validates the committed real-world snapshots - including the era-coverage test:
/// every draw in ~24 years of history must fit its known rule era, so an
/// undocumented matrix change (or bad feed data) fails here, loudly.
/// </summary>
public class SnapshotHistoryTests
{
    private static readonly SnapshotHistorySource Source = new();

    [Theory]
    [InlineData(Game.Powerball, 1900)]
    [InlineData(Game.MegaMillions, 2400)]
    public async Task Snapshot_LoadsSubstantialHistory(Game game, int minimumDraws)
    {
        var history = await Source.GetHistoryAsync(game, CancellationToken.None);
        Assert.True(history.Count >= minimumDraws, $"Only {history.Count} draws loaded.");
    }

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public async Task EntireHistory_FitsKnownRuleEras(Game game)
    {
        var history = await Source.GetHistoryAsync(game, CancellationToken.None);
        var violations = EraValidator.ValidateHistory(history);

        Assert.True(violations.Count == 0,
            "Era violations found - either the era table is missing a rule change or the feed data is bad:\n"
            + string.Join("\n", violations.Take(10).Select(v => $"  {v.DrawDate:yyyy-MM-dd}: {v.Reason}")));
    }

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public async Task History_HasNoDuplicateDrawDates(Game game)
    {
        var history = await Source.GetHistoryAsync(game, CancellationToken.None);
        var duplicates = history.GroupBy(d => d.DrawDate).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task History_IsOrderedAscending()
    {
        var history = await Source.GetHistoryAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(history.OrderBy(d => d.DrawDate), history);
    }

    [Fact]
    public void Name_IdentifiesTheSourceInTheImportLedger()
    {
        // This string is written to ImportLedger.Source on first boot, and is how
        // a later reader tells a snapshot seed apart from a live import. Changing
        // it silently orphans the provenance of every row already recorded.
        Assert.Equal("snapshot:data.ny.gov", Source.Name);
    }
}
