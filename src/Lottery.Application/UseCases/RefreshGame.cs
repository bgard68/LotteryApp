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
            try
            {
                var fetched = await numbersFeed.GetDrawsAfterAsync(game, latest?.DrawDate ?? DateOnly.MinValue, ct);
                foreach (var draw in fetched)
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
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                feedError = ex.Message;
            }
        }

        var jackpotUpdated = await RefreshJackpotAsync(game, ct);

        var nowLatest = await draws.GetLatestAsync(game, ct);
        var upToDate = nowLatest is not null && nowLatest.DrawDate >= lastScheduled;

        return new RefreshResult(game, upToDate, newDraws, skipped, jackpotUpdated, feedError);
    }

    private async Task<bool> RefreshJackpotAsync(Game game, CancellationToken ct)
    {
        JackpotInfo? info;
        try
        {
            info = await jackpotFeed.GetJackpotAsync(game, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }

        if (info is null)
            return false;

        if (info.NextEstimatedJackpot is not null || info.NextCashValue is not null)
        {
            await jackpotStore.SaveAsync(new JackpotEstimate(
                game, info.NextEstimatedJackpot, info.NextCashValue, time.GetUtcNow()), ct);
        }

        if (info is { LastDrawDate: not null, LastJackpot: not null })
        {
            await draws.UpdateJackpotAsync(game, info.LastDrawDate.Value,
                info.LastJackpot, info.LastJackpotWon, ct);
        }

        return true;
    }
}
