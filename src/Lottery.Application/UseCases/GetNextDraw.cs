using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record NextDrawResult(Game Game, DateTimeOffset DrawTimeUtc, DateOnly DrawDate);

public sealed class GetNextDraw(TimeProvider time)
{
    public NextDrawResult Execute(Game game)
    {
        var nowUtc = time.GetUtcNow();
        var next = DrawSchedule.NextDrawUtc(game, nowUtc);
        var nextEastern = TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        return new NextDrawResult(game, next, DateOnly.FromDateTime(nextEastern.DateTime));
    }
}
