namespace Lottery.Domain;

public enum Game
{
    Powerball,
    MegaMillions,
}

/// <summary>
/// Convenience accessors over <see cref="GameProfiles"/>. These used to hold the per-game facts
/// themselves, in three parallel <c>switch</c> expressions; the data now lives in one profile per
/// game and these are the shorthand callers already use. A game with no registered profile throws
/// here, exactly as the old switches' default arm did.
/// </summary>
public static class GameExtensions
{
    /// <summary>Days of week (Eastern Time) on which the game draws.</summary>
    public static IReadOnlyList<DayOfWeek> DrawDays(this Game game) => GameProfiles.For(game).DrawDays;

    /// <summary>Local (Eastern Time) wall-clock time of the drawing.</summary>
    public static TimeOnly DrawTimeEastern(this Game game) => GameProfiles.For(game).DrawTimeEastern;

    public static string SpecialBallName(this Game game) => GameProfiles.For(game).SpecialBallName;

    /// <summary>Canonical URL segment for the game.</summary>
    public static string Slug(this Game game) => GameProfiles.For(game).Slug;
}
