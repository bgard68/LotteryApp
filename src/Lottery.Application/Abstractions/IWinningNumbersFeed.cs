using Lottery.Domain;

namespace Lottery.Application.Abstractions;

/// <summary>
/// The outcome of asking a feed for draws: either the draws, or the reason the source could not
/// supply them. Never both.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to delete a recurring defect rather than to tidy anything. The port used to
/// report failure by <em>throwing</em>, which made "which failures are expected" a property of
/// exception types — so <c>RefreshGame</c> carried a filter listing the types Infrastructure
/// happened to throw, and the Infrastructure adapters carried filters converting their own
/// exceptions into types that filter would match.
/// </para>
/// <para>
/// Three times that arrangement failed the same way: a payload produced a type nobody had listed
/// (<c>IndexOutOfRangeException</c> from a short Powerball row; <c>ArgumentException</c> rather
/// than the derived <c>ArgumentOutOfRangeException</c> from <c>Draw.Create</c>; an
/// <c>XmlException</c> from a bot-challenge page served with a 200), it escaped both filters, and
/// <c>/internal/refresh</c> — the endpoint the keep-alive workflow calls — answered 500, which
/// reads as the whole instance being down. The repository's own test for it is named
/// <c>AnUnanticipatedFeedFailure_IsReported_NotA500</c>.
/// </para>
/// <para>
/// An allowlist of exception types cannot be made exhaustive, because the set of types a third
/// party's payload can provoke is not knowable. Making failure part of the return value removes
/// the question: the caller handles one shape, there is nothing to enumerate, and a type nobody
/// anticipated cannot select the wrong branch because no branch is selected by type.
/// </para>
/// </remarks>
public sealed record WinningNumbersResult(IReadOnlyList<Draw> Draws, string? Error)
{
    public bool Succeeded => Error is null;

    public static WinningNumbersResult Ok(IReadOnlyList<Draw> draws) => new(draws, null);

    /// <summary>The source was unreachable, or returned something unusable.</summary>
    public static WinningNumbersResult Unavailable(string error) => new([], error);
}

/// <summary>Live source of winning numbers, used for incremental refresh and gap-repair.</summary>
public interface IWinningNumbersFeed
{
    /// <summary>
    /// Draws strictly after the given date, ascending — or the reason the source could not be
    /// read. Implementations report source problems in the result and do not throw for them;
    /// cancellation still propagates as <see cref="OperationCanceledException"/>, because a
    /// shutdown is not a feed failure.
    /// </summary>
    Task<WinningNumbersResult> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct);
}
