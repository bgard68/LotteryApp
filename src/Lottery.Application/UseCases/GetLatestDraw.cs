using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record LatestDrawResult(
    Game Game,
    DrawStatus Status,
    DateOnly DrawDate,
    IReadOnlyList<int>? WhiteBalls,
    int? Special,
    decimal? JackpotAmount,
    bool? JackpotWon);

public sealed class GetLatestDraw(IDrawRepository draws, TimeProvider time)
{
    /// <remarks>
    /// Returns non-null always. It was declared nullable and never returned null, so the endpoint
    /// silenced it with <c>result!</c> - a null-forgiving operator standing in for a signature
    /// nobody had corrected, which would have become a NullReferenceException the day the
    /// signature started telling the truth.
    /// </remarks>
    public async Task<LatestDrawResult> ExecuteAsync(Game game, CancellationToken ct)
    {
        var latest = await draws.GetLatestAsync(game, ct);
        var lastScheduled = DrawSchedule.PreviousDrawDate(game, time.GetUtcNow());

        // A drawing the schedule says has happened, with no stored numbers yet,
        // is Pending - never silently presented as if the previous draw were newest.
        if (latest is null)
            return new LatestDrawResult(game, DrawStatus.Pending, lastScheduled, null, null, null, null);

        if (lastScheduled > latest.DrawDate)
            return new LatestDrawResult(game, DrawStatus.Pending, lastScheduled, null, null, null, null);

        return new LatestDrawResult(game, DrawStatus.Published, latest.DrawDate,
            latest.WhiteBalls, latest.Special, latest.JackpotAmount, latest.JackpotWon);
    }
}
