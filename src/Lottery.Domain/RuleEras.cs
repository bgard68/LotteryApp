namespace Lottery.Domain;

/// <summary>
/// Known matrix eras, reference data maintained by hand from the games' documented
/// rule changes. The era-coverage test validates the entire imported history against
/// this table, so an unknown rule change fails loudly instead of mis-validating.
/// </summary>
public static class RuleEras
{
    public static readonly IReadOnlyList<RuleEra> All =
    [
        // Powerball
        new(Game.Powerball, new DateOnly(1992, 4, 22), 45, 45),
        new(Game.Powerball, new DateOnly(1997, 11, 5), 49, 42),
        new(Game.Powerball, new DateOnly(2002, 10, 9), 53, 42),
        new(Game.Powerball, new DateOnly(2005, 8, 28), 55, 42),
        new(Game.Powerball, new DateOnly(2009, 1, 7), 59, 39),
        new(Game.Powerball, new DateOnly(2012, 1, 15), 59, 35),
        new(Game.Powerball, new DateOnly(2015, 10, 7), 69, 26),

        // Mega Millions
        new(Game.MegaMillions, new DateOnly(2002, 5, 17), 52, 52),
        new(Game.MegaMillions, new DateOnly(2005, 6, 24), 56, 46),
        new(Game.MegaMillions, new DateOnly(2013, 10, 22), 75, 15),
        new(Game.MegaMillions, new DateOnly(2017, 10, 31), 70, 25),
        new(Game.MegaMillions, new DateOnly(2025, 4, 8), 70, 24),
    ];

    /// <summary>
    /// The era in force for a game on a given draw date, or false when none covers it.
    /// </summary>
    /// <remarks>
    /// Selects the latest matching era by <c>EffectiveFrom</c> rather than by position. The
    /// previous implementation walked <see cref="All"/> keeping the last match, which was correct
    /// only while the table stayed sorted ascending per game — an invariant a comment asserted and
    /// nothing enforced, so inserting a backfilled era in the wrong place would have silently
    /// validated draws against the wrong matrix.
    /// </remarks>
    public static bool TryForDate(Game game, DateOnly drawDate, out RuleEra era)
    {
        RuleEra? best = null;
        foreach (var candidate in All)
        {
            if (candidate.Game != game || candidate.EffectiveFrom > drawDate)
                continue;

            if (best is null || candidate.EffectiveFrom > best.EffectiveFrom)
                best = candidate;
        }

        era = best!;
        return best is not null;
    }

    /// <summary>The era in force for a game on a given draw date.</summary>
    public static RuleEra ForDate(Game game, DateOnly drawDate) =>
        TryForDate(game, drawDate, out var era)
            ? era
            : throw new ArgumentOutOfRangeException(nameof(drawDate), drawDate,
                $"No known {game} rule era covers {drawDate:yyyy-MM-dd}.");

    public static RuleEra Current(Game game, DateOnly today) => ForDate(game, today);
}
