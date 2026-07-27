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
    public async Task<LatestDrawResult?> ExecuteAsync(Game game, CancellationToken ct)
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
