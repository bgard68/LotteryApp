using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

/// <summary>
/// The card's countdown comes from schedule math, its dollar figures from a
/// feed that is allowed to fail. The edge case that matters is the second one
/// going missing without taking the first one down with it.
/// </summary>
public class GetNextDrawTests
{
    // Monday 2026-07-27, noon Eastern (16:00 UTC) - before that night's drawing.
    private static readonly DateTimeOffset MondayNoonEt = new(2026, 7, 27, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WithNoStoredEstimate_AmountsAreNullButTheDateStands()
    {
        var result = await new GetNextDraw(new FakeTimeProvider(MondayNoonEt), new FakeJackpotStore())
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Null(result.EstimatedJackpot);
        Assert.Null(result.CashValue);
        Assert.Null(result.JackpotUpdatedAtUtc);
        // The whole point: a dead jackpot feed never costs you the countdown.
        Assert.Equal(new DateOnly(2026, 7, 27), result.DrawDate);
        Assert.True(result.DrawTimeUtc > MondayNoonEt);
    }

    [Fact]
    public async Task StoredEstimateIsSurfaced()
    {
        var store = new FakeJackpotStore();
        await store.SaveAsync(new JackpotEstimate(Game.Powerball, 633_000_000m, 277_300_000m, MondayNoonEt), CancellationToken.None);

        var result = await new GetNextDraw(new FakeTimeProvider(MondayNoonEt), store)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(633_000_000m, result.EstimatedJackpot);
        Assert.Equal(277_300_000m, result.CashValue);
        Assert.Equal(MondayNoonEt, result.JackpotUpdatedAtUtc);
    }

    [Fact]
    public async Task AnotherGamesEstimateIsNotBorrowed()
    {
        var store = new FakeJackpotStore();
        await store.SaveAsync(new JackpotEstimate(Game.MegaMillions, 800_000_000m, 344_200_000m, MondayNoonEt), CancellationToken.None);

        var result = await new GetNextDraw(new FakeTimeProvider(MondayNoonEt), store)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Null(result.EstimatedJackpot);
        Assert.Equal(Game.Powerball, result.Game);
    }

    [Fact]
    public async Task ImmediatelyAfterADrawing_TheNextOneIsAlreadyAhead()
    {
        // 23:30 ET on a Powerball night: that night's 22:59 draw has passed, so
        // "next" must roll to Wednesday rather than pointing at a past instant.
        var justAfterDraw = new DateTimeOffset(2026, 7, 28, 3, 30, 0, TimeSpan.Zero);

        var result = await new GetNextDraw(new FakeTimeProvider(justAfterDraw), new FakeJackpotStore())
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.DrawTimeUtc > justAfterDraw, "next draw must always be in the future");
        Assert.Equal(new DateOnly(2026, 7, 29), result.DrawDate);
    }

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public async Task NextDrawIsAlwaysInTheFuture(Game game)
    {
        // Sweep a full week of hours - no hour of any day may yield a past draw.
        for (var hour = 0; hour < 24 * 7; hour++)
        {
            var now = MondayNoonEt.AddHours(hour);
            var result = await new GetNextDraw(new FakeTimeProvider(now), new FakeJackpotStore())
                .ExecuteAsync(game, CancellationToken.None);

            Assert.True(result.DrawTimeUtc > now, $"{game} at {now:u} produced a draw at {result.DrawTimeUtc:u}");
        }
    }
}
