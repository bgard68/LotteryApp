using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The decorators that hold the "an external source must never take the app down" policy.
/// </summary>
/// <remarks>
/// This is the component that replaced a pair of hand-written exception allowlists - one in each
/// adapter, one in RefreshGame - which between them were wrong three times, each time surfacing as
/// a 500 from the endpoint the keep-alive workflow polls. So the tests that matter here are the
/// two edges of the policy: a type nobody could have anticipated is still converted, and
/// cancellation is still NOT, because a shutdown is not a feed failure.
/// </remarks>
public class FaultTolerantFeedsTests
{
    private sealed class ThrowingNumbersFeed(Exception toThrow) : IWinningNumbersFeed
    {
        public Task<WinningNumbersResult> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct) =>
            throw toThrow;
    }

    private sealed class ThrowingJackpotFeed(Exception toThrow) : IJackpotFeed
    {
        public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) => throw toThrow;
    }

    private static FaultTolerantWinningNumbersFeed Numbers(Exception toThrow) =>
        new(new ThrowingNumbersFeed(toThrow), NullLogger<FaultTolerantWinningNumbersFeed>.Instance);

    private static FaultTolerantJackpotFeed Jackpot(Exception toThrow) =>
        new(new ThrowingJackpotFeed(toThrow), NullLogger<FaultTolerantJackpotFeed>.Instance);

    // Deliberately exotic: the point of the decorator is that the type does not need to be
    // known in advance. Each of these stands in for one of the three that actually escaped -
    // IndexOutOfRangeException from a short Powerball row, the base ArgumentException from
    // Draw.Create, XmlException from a bot-challenge page served with a 200 - plus the
    // resilience pipeline's own exceptions, which no allowlist had ever named.
    public static TheoryData<Exception> Unanticipated =>
    [
        new IndexOutOfRangeException("index"),
        new ArgumentException("argument"),
        new System.Xml.XmlException("xml"),
        new NotSupportedException("brand new"),
        new InvalidProgramException("absurd but still not a 500"),
    ];

    [Theory]
    [MemberData(nameof(Unanticipated))]
    public async Task NumbersFeed_ConvertsAnyUnanticipatedFailure_IntoAReportedOne(Exception thrown)
    {
        var result = await Numbers(thrown)
            .GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Draws);
        // The cause is named, so a degraded cycle is still diagnosable from the response.
        Assert.Contains(thrown.GetType().Name, result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Unanticipated))]
    public async Task JackpotFeed_ConvertsAnyUnanticipatedFailure_IntoNoFigure(Exception thrown)
    {
        Assert.Null(await Jackpot(thrown).GetJackpotAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task NumbersFeed_LetsCancellationThrough()
    {
        // Not a feed failure: the host is stopping or the caller gave up. Reporting it as a feed
        // error would have the refresh carry on with a cancelled token and log a source problem
        // that never happened - which is what the old filter did, since it listed
        // TaskCanceledException among the failures to swallow.
        var feed = Numbers(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));
    }

    [Fact]
    public async Task JackpotFeed_LetsCancellationThrough()
    {
        var feed = Jackpot(new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            feed.GetJackpotAsync(Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task NumbersFeed_PassesASuccessfulResultThrough()
    {
        var draw = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);
        var inner = new StubNumbersFeed(WinningNumbersResult.Ok([draw]));
        var feed = new FaultTolerantWinningNumbersFeed(
            inner, NullLogger<FaultTolerantWinningNumbersFeed>.Instance);

        var result = await feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 21), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Same(draw, Assert.Single(result.Draws));
    }

    private sealed class StubNumbersFeed(WinningNumbersResult result) : IWinningNumbersFeed
    {
        public Task<WinningNumbersResult> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct) =>
            Task.FromResult(result);
    }
}
