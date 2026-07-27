namespace Lottery.Domain;

public enum Game
{
    Powerball,
    MegaMillions,
}

public static class GameExtensions
{
    /// <summary>Days of week (Eastern Time) on which the game draws.</summary>
    public static IReadOnlyList<DayOfWeek> DrawDays(this Game game) => game switch
    {
        Game.Powerball => [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday],
        Game.MegaMillions => [DayOfWeek.Tuesday, DayOfWeek.Friday],
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    /// <summary>Local (Eastern Time) wall-clock time of the drawing.</summary>
    public static TimeOnly DrawTimeEastern(this Game game) => game switch
    {
        Game.Powerball => new TimeOnly(22, 59),
        Game.MegaMillions => new TimeOnly(23, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    public static string SpecialBallName(this Game game) => game switch
    {
        Game.Powerball => "Powerball",
        Game.MegaMillions => "Mega Ball",
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
}
