using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.Extensions.Logging;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// The one definition of "an external source failed", applied by the decorators below.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate catch-all, which is normally the wrong instinct — and is the right one here for a
/// specific reason. The previous arrangement was the opposite: each adapter listed the exception
/// types it expected, and the Application use case listed them again so it could tell a feed
/// failure from a real fault. An allowlist has to be exhaustive to be correct, and the set of
/// types a third party's payload can provoke is not knowable, so it was wrong three times — each
/// time surfacing as a 500 from the endpoint the keep-alive workflow polls.
/// </para>
/// <para>
/// What makes the catch-all safe is where it sits and what it does. It wraps exactly one call to
/// one external source, at the boundary; it converts to a <em>reported</em> outcome rather than
/// swallowing — the reason reaches the caller and the log — and it re-throws cancellation
/// untouched, because a shutdown or a disconnected client is not a feed problem and must keep
/// propagating. The same policy already existed at the HTTP endpoint as a last-ditch net
/// (<c>catch (Exception ex) when (ex is not OperationCanceledException)</c>); this moves it to the
/// boundary that actually owns the risk, so the endpoint's net stops being load-bearing.
/// </para>
/// </remarks>
internal static class FeedFailurePolicy
{
    /// <summary>
    /// True when <paramref name="exception"/> is a source problem to be reported. Cancellation
    /// never is: it means the host is stopping or the caller gave up.
    /// </summary>
    public static bool IsSourceFailure(Exception exception) => exception is not OperationCanceledException;

    /// <summary>Type name plus message — enough to diagnose without leaking a stack trace.</summary>
    public static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}

/// <summary>
/// Guarantees <see cref="IWinningNumbersFeed"/>'s contract — source problems reported, never
/// thrown — for any implementation, including one whose author forgot.
/// </summary>
public sealed class FaultTolerantWinningNumbersFeed(
    IWinningNumbersFeed inner,
    ILogger<FaultTolerantWinningNumbersFeed> logger) : IWinningNumbersFeed
{
    public async Task<WinningNumbersResult> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct)
    {
        try
        {
            return await inner.GetDrawsAfterAsync(game, after, ct);
        }
        catch (Exception ex) when (FeedFailurePolicy.IsSourceFailure(ex))
        {
            // Warning, not error: a flaky public feed is an expected condition the app is designed
            // to ride out. It is logged because "degraded" and "fine" must not look identical.
            logger.LogWarning(ex, "{Game}: winning-numbers feed failed; reporting it as unavailable.", game);
            return WinningNumbersResult.Unavailable(FeedFailurePolicy.Describe(ex));
        }
    }
}

/// <summary>
/// Same guarantee for <see cref="IJackpotFeed"/>, whose "unavailable" is a null
/// <see cref="JackpotInfo"/>.
/// </summary>
/// <remarks>
/// Each jackpot adapter still handles the payload shapes it knows about, because returning null
/// for a response that parsed but carried no jackpot is a parse result rather than a failure. This
/// decorator covers what they do not anticipate, which is the part no adapter can enumerate for
/// itself — and means a newly added source cannot take the refresh cycle down by omitting a
/// try/catch.
/// </remarks>
public sealed class FaultTolerantJackpotFeed(
    IJackpotFeed inner,
    ILogger<FaultTolerantJackpotFeed> logger) : IJackpotFeed
{
    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        try
        {
            return await inner.GetJackpotAsync(game, ct);
        }
        catch (Exception ex) when (FeedFailurePolicy.IsSourceFailure(ex))
        {
            logger.LogWarning(ex, "{Game}: jackpot feed failed; continuing without an amount.", game);
            return null;
        }
    }
}
