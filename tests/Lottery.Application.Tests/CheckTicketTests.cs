using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

public class CheckTicketTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero));

    private static FakeDrawRepository RepoWithSaturdayDraw()
    {
        var repo = new FakeDrawRepository();
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18));
        return repo;
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4 }, 5, "Exactly 5")]           // too few whites
    [InlineData(new[] { 1, 2, 3, 4, 4 }, 5, "distinct")]         // duplicate
    [InlineData(new[] { 1, 2, 3, 4, 70 }, 5, "between 1 and 69")] // out of current PB era
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 27, "between 1 and 26")] // special out of range
    public async Task InvalidTickets_AreRejectedWithReason(int[] whites, int special, string expectedFragment)
    {
        var result = await new CheckTicket(RepoWithSaturdayDraw(), Time)
            .ExecuteAsync(Game.Powerball, whites, special, CancellationToken.None);

        Assert.Equal(CheckStatus.InvalidTicket, result.Status);
        Assert.Contains(expectedFragment, result.Error);
    }

    [Fact]
    public async Task EmptyDatabase_IsDataUnavailable_NotZeroMatches()
    {
        var result = await new CheckTicket(new FakeDrawRepository(), Time)
            .ExecuteAsync(Game.Powerball, [1, 2, 3, 4, 5], 6, CancellationToken.None);

        Assert.Equal(CheckStatus.DataUnavailable, result.Status);
    }

    [Fact]
    public async Task WinningMatch_IsTiered()
    {
        var result = await new CheckTicket(RepoWithSaturdayDraw(), Time)
            .ExecuteAsync(Game.Powerball, [7, 19, 33, 1, 2], 18, CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        var match = Assert.Single(result.Matches);
        Assert.Equal("Match 3 + Powerball", match.TierName);
        Assert.Equal(100m, match.ApproximateAmount);
    }

    [Fact]
    public async Task WinningMatch_CarriesTheDrawnNumbersBack()
    {
        // The UI highlights which of the ticket's numbers hit, which it can only
        // do if each match carries the drawing's own numbers alongside the counts.
        var result = await new CheckTicket(RepoWithSaturdayDraw(), Time)
            .ExecuteAsync(Game.Powerball, [7, 19, 33, 1, 2], 18, CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal([7, 19, 33, 51, 64], match.DrawnWhiteBalls);
        Assert.Equal(18, match.DrawnSpecial);
    }

    [Fact]
    public async Task OkResult_ReportsTheHistoryItWasCheckedAgainst()
    {
        // "No wins" only means something next to how much history was searched,
        // so the count and the oldest draw date travel back with the answer.
        var repo = RepoWithSaturdayDraw();
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2015, 10, 7), [1, 2, 3, 4, 5], 6));

        var result = await new CheckTicket(repo, Time)
            .ExecuteAsync(Game.Powerball, [7, 19, 33, 1, 2], 18, CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(2, result.DrawsChecked);
        Assert.Equal(new DateOnly(2015, 10, 7), result.HistorySince);
    }

    [Fact]
    public async Task NonWinningPartialMatch_IsExcluded()
    {
        // 2 whites, no special: below every prize tier.
        var result = await new CheckTicket(RepoWithSaturdayDraw(), Time)
            .ExecuteAsync(Game.Powerball, [7, 19, 1, 2, 3], 5, CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Empty(result.Matches);
        Assert.Equal(1, result.DrawsChecked);
    }
}
