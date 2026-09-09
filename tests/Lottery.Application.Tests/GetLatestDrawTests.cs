using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

public class GetLatestDrawTests
{
    private static readonly Draw SaturdayDraw = Draw.Create(
        Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);

    [Fact]
    public async Task BeforeNextDraw_LatestStoredIsPublished()
    {
        var repo = new FakeDrawRepository();
        repo.Draws.Add(SaturdayDraw);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero)); // Mon noon ET

        var result = await new GetLatestDraw(repo, time).ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(DrawStatus.Published, result!.Status);
        Assert.Equal(new DateOnly(2026, 7, 25), result.DrawDate);
        Assert.Equal([7, 19, 33, 51, 64], result.WhiteBalls);
    }

    [Fact]
    public async Task AfterDrawTime_WithoutStoredResult_IsPending()
    {
        var repo = new FakeDrawRepository();
        repo.Draws.Add(SaturdayDraw);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 25, 16, 0, 0, TimeSpan.Zero));

        // Advance the fake clock past Monday's drawing with no new row stored -
        // the Monday draw must surface as Pending, not silently show Saturday as newest.
        time.Advance(TimeSpan.FromDays(2) + TimeSpan.FromHours(11));

        var result = await new GetLatestDraw(repo, time).ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(DrawStatus.Pending, result!.Status);
        Assert.Equal(new DateOnly(2026, 7, 27), result.DrawDate);
        Assert.Null(result.WhiteBalls);
    }

    [Fact]
    public async Task PublishedResult_CarriesTheSpecialBallAndJackpotFacts()
    {
        // The result is a projection of the stored draw; the special ball and the
        // jackpot facts sit beside the white balls on the card, and a mapping that
        // dropped or crossed them would look correct until someone read it.
        var repo = new FakeDrawRepository();
        repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18,
            jackpotAmount: 214_000_000m, jackpotWon: true));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero)); // Mon noon ET

        var result = await new GetLatestDraw(repo, time).ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(Game.Powerball, result!.Game);
        Assert.Equal(DrawStatus.Published, result.Status);
        Assert.Equal(18, result.Special);
        Assert.Equal(214_000_000m, result.JackpotAmount);
        Assert.True(result.JackpotWon);
    }

    [Fact]
    public async Task EmptyDatabase_IsPending()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero));
        var result = await new GetLatestDraw(new FakeDrawRepository(), time).ExecuteAsync(Game.Powerball, CancellationToken.None);
        Assert.Equal(DrawStatus.Pending, result!.Status);
    }
}
