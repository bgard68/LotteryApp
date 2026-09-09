using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

/// <summary>
/// Edges the base CheckTicketTests leaves open: match ordering (newest first),
/// game isolation on the check side, current-era Mega Millions bounds with the
/// game's own special-ball name, and validation short-circuiting ahead of any
/// data access.
/// </summary>
public class CheckTicketResultShapeTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero));

    // Ticket under test throughout: 7,19,33,51,64 + special 18.
    private static readonly int[] TicketWhites = [7, 19, 33, 51, 64];
    private const int TicketSpecial = 18;

    private static FakeDrawRepository ThreeDrawRepo()
    {
        var repo = new FakeDrawRepository();
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18)); // jackpot vs ticket
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [1, 2, 7, 19, 33], 18));   // 3 whites + special
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2026, 7, 20), [1, 2, 3, 7, 19], 5));     // 2 whites, no special: no prize
        return repo;
    }

    [Fact]
    public async Task Matches_AreOrderedNewestFirst_AndNonWinnersExcluded()
    {
        var checker = new CheckTicket(ThreeDrawRepo(), Time);

        var result = await checker.ExecuteAsync(Game.Powerball, TicketWhites, TicketSpecial, CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(new DateOnly(2026, 7, 25), result.Matches[0].DrawDate);
        Assert.Equal(new DateOnly(2026, 7, 22), result.Matches[1].DrawDate);
    }

    [Fact]
    public async Task OtherGamesDraws_AreNeverChecked()
    {
        var repo = ThreeDrawRepo();
        // Identical numbers under the other game: must not appear as a Powerball hit.
        repo.Draws.Add(Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 24), [7, 19, 33, 51, 64], 18));

        var result = await new CheckTicket(repo, Time)
            .ExecuteAsync(Game.Powerball, TicketWhites, TicketSpecial, CancellationToken.None);

        Assert.Equal(3, result.DrawsChecked);
        Assert.Equal(2, result.Matches.Count);
        Assert.All(result.Matches, m => Assert.NotEqual(new DateOnly(2026, 7, 24), m.DrawDate));
    }

    [Fact]
    public async Task MegaMillions_SpecialBound_UsesMegaBallNameAndCurrentEra()
    {
        // Current MM era (since 2025-04-08) is 5/70 + 1/24, so 25 is out.
        var result = await new CheckTicket(new FakeDrawRepository(), Time)
            .ExecuteAsync(Game.MegaMillions, [1, 2, 3, 4, 5], 25, CancellationToken.None);

        Assert.Equal(CheckStatus.InvalidTicket, result.Status);
        Assert.Equal("Mega Ball must be between 1 and 24.", result.Error);
    }

    [Fact]
    public async Task MegaMillions_WhiteBound_IsTheCurrentSeventyBallMatrix()
    {
        var result = await new CheckTicket(new FakeDrawRepository(), Time)
            .ExecuteAsync(Game.MegaMillions, [1, 2, 3, 4, 71], 5, CancellationToken.None);

        Assert.Equal(CheckStatus.InvalidTicket, result.Status);
        Assert.Equal("White balls must be between 1 and 70.", result.Error);
    }

    [Fact]
    public async Task InvalidTicket_ShortCircuitsBeforeTouchingTheRepository()
    {
        // Validation happens before any data access, so a bad ticket against an
        // empty database reports InvalidTicket - not DataUnavailable.
        var result = await new CheckTicket(new FakeDrawRepository(), Time)
            .ExecuteAsync(Game.Powerball, [1, 2, 3, 4], 5, CancellationToken.None);

        Assert.Equal(CheckStatus.InvalidTicket, result.Status);
        Assert.Equal(0, result.DrawsChecked);
        Assert.Null(result.HistorySince);
        Assert.Empty(result.Matches);
    }
}
