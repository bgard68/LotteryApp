namespace Lottery.Domain;

/// <summary>
/// Pure schedule math: when drawings occur. Draw times are defined in Eastern Time,
/// so the ET wall-clock time is built first and then converted to UTC via the tz
/// database, which makes DST transitions the timezone's problem rather than ours.
/// Callers supply "now"; this type never reads a clock.
/// </summary>
public static class DrawSchedule
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>UTC instant of the next drawing strictly after <paramref name="nowUtc"/>.</summary>
    public static DateTimeOffset NextDrawUtc(Game game, DateTimeOffset nowUtc)
    {
        var nowEastern = TimeZoneInfo.ConvertTime(nowUtc, Eastern);
        var date = DateOnly.FromDateTime(nowEastern.DateTime);

        for (var i = 0; i <= 7; i++)
        {
            var candidate = date.AddDays(i);
            if (!game.DrawDays().Contains(candidate.DayOfWeek))
                continue;

            var utc = DrawInstantUtc(game, candidate);
            if (utc > nowUtc)
                return utc;
        }

        throw new InvalidOperationException("No draw day found within 8 days - unreachable.");
    }

    /// <summary>Draw date of the most recent drawing at or before <paramref name="nowUtc"/>.</summary>
    public static DateOnly PreviousDrawDate(Game game, DateTimeOffset nowUtc)
    {
        var nowEastern = TimeZoneInfo.ConvertTime(nowUtc, Eastern);
        var date = DateOnly.FromDateTime(nowEastern.DateTime);

        for (var i = 0; i <= 7; i++)
        {
            var candidate = date.AddDays(-i);
            if (!game.DrawDays().Contains(candidate.DayOfWeek))
                continue;

            if (DrawInstantUtc(game, candidate) <= nowUtc)
                return candidate;
        }

        throw new InvalidOperationException("No draw day found within 8 days - unreachable.");
    }

    /// <summary>UTC instant of the drawing held on the given (Eastern) draw date.</summary>
    public static DateTimeOffset DrawInstantUtc(Game game, DateOnly drawDate)
    {
        var wallClock = drawDate.ToDateTime(game.DrawTimeEastern(), DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, Eastern.GetUtcOffset(wallClock)).ToUniversalTime();
    }
}
