using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record NextDrawResult(
    Game Game,
    DateTimeOffset DrawTimeUtc,
    DateOnly DrawDate,
    decimal? EstimatedJackpot,
    decimal? CashValue,
    DateTimeOffset? JackpotUpdatedAtUtc);

public sealed class GetNextDraw(TimeProvider time, IJackpotStore jackpots)
{
    public async Task<NextDrawResult> ExecuteAsync(Game game, CancellationToken ct)
    {
        var nowUtc = time.GetUtcNow();
        var next = DrawSchedule.NextDrawUtc(game, nowUtc);
        var nextEastern = TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        // Jackpot amounts are best-effort by design; null simply hides them in the UI.
        var estimate = await jackpots.GetAsync(game, ct);

        return new NextDrawResult(game, next, DateOnly.FromDateTime(nextEastern.DateTime),
            estimate?.NextEstimatedJackpot, estimate?.NextCashValue, estimate?.UpdatedAtUtc);
    }
}
