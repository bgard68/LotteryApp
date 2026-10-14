using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record RefreshResult(
    Game Game,
    bool UpToDate,
    int NewDraws,
    int SkippedInvalid,
    bool JackpotUpdated,
    string? FeedError);

/// <summary>
/// One refresh cycle for a game: gap-repair winning numbers from the live feed
/// (everything after the latest stored draw), then refresh jackpot data.
/// Feed failures are reported, never thrown - a broken external source must not
/// take the app down, and jackpot data is optional by design.
/// </summary>
/// <remarks>
/// <para>
/// That promise used to be kept by a <c>catch</c> filter listing the exception types the feeds
/// were known to throw. It was broken three times by types nobody had listed, each time turning
/// into a 500 from <c>/internal/refresh</c>. The feeds now report source problems in their return
/// value (<see cref="WinningNumbersResult"/>), so this class makes the promise by <em>not
/// catching anything</em> — there is no list left to be incomplete, and no exception type that
/// could select the wrong branch.
/// </para>
/// <para>
/// A second consequence of the old shape: the repository calls sat inside the same <c>try</c>, so
/// a database failure during the write loop was reported to the caller as <c>FeedError</c> — a
/// fault blamed on an innocent third party, and a 200 where a 500 belonged. Persistence now runs
/// outside any failure handling, which is correct: a database that cannot accept a write is this
/// system's problem and must surface as one.
/// </para>
/// </remarks>
public sealed class RefreshGame(
    IDrawRepository draws,
    IWinningNumbersFeed numbersFeed,
    IJackpotFeed jackpotFeed,
    IJackpotStore jackpotStore,
    TimeProvider time)
{
    public async Task<RefreshResult> ExecuteAsync(Game game, CancellationToken ct)
    {
        var newDraws = 0;
        var skipped = 0;
        string? feedError = null;

        var latest = await draws.GetLatestAsync(game, ct);
        var lastScheduled = DrawSchedule.PreviousDrawDate(game, time.GetUtcNow());
        var behind = latest is null || latest.DrawDate < lastScheduled;

        if (behind)
        {
            var fetched = await numbersFeed.GetDrawsAfterAsync(game, latest?.DrawDate ?? DateOnly.MinValue, ct);
            if (fetched.Succeeded)
            {
                foreach (var draw in fetched.Draws)
                {
                    // Era-validate each live draw. Invalid rows are skipped, not
                    // stored - the weekly era-check run surfaces persistent skips.
                    if (EraValidator.Validate(draw) is not null)
                    {
                        skipped++;
                        continue;
                    }

                    if (await draws.UpsertAsync(draw, ct))
                        newDraws++;
                }
            }
            else
            {
                feedError = fetched.Error;
            }
        }

        var jackpotUpdated = await RefreshJackpotAsync(game, ct);

        var nowLatest = await draws.GetLatestAsync(game, ct);
        var upToDate = nowLatest is not null && nowLatest.DrawDate >= lastScheduled;

        return new RefreshResult(game, upToDate, newDraws, skipped, jackpotUpdated, feedError);
    }

    /// <remarks>
    /// No <c>try</c> here either: <see cref="IJackpotFeed"/> signals "no figure available" with a
    /// null, and the adapters are wrapped so nothing escapes as an exception. The previous version
    /// caught three types and returned false without logging, so a persistent jackpot-source
    /// outage was indistinguishable from "no new amount" — silent by construction.
    /// </remarks>
    private async Task<bool> RefreshJackpotAsync(Game game, CancellationToken ct)
    {
        var info = await jackpotFeed.GetJackpotAsync(game, ct);
        if (info is null)
            return false;

        // Zero is not a jackpot, it is a placeholder: sources publish the next
        // draw before its amount is announced, and a plain null check accepted
        // that - replacing a good stored estimate with 0, which the site then
        // rendered as "Estimated jackpot $0".
        //
        // Guarded here as well as in the feed so no source can poison the store
        // this way. Skipping the save leaves the previous estimate in place,
        // which is the right thing to keep showing until the real figure lands.
        var estimate = info.NextEstimatedJackpot is > 0 ? info.NextEstimatedJackpot : null;
        var cashValue = info.NextCashValue is > 0 ? info.NextCashValue : null;

        if (estimate is not null || cashValue is not null)
        {
            await jackpotStore.SaveAsync(new JackpotEstimate(
                game, estimate, cashValue, time.GetUtcNow()), ct);
        }

        if (info is { LastDrawDate: not null, LastJackpot: not null })
        {
            await draws.UpdateJackpotAsync(game, info.LastDrawDate.Value,
                info.LastJackpot, info.LastJackpotWon, ct);
        }

        return true;
    }
}
