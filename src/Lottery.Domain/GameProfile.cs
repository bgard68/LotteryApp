using System.Collections.Frozen;

namespace Lottery.Domain;

/// <summary>
/// Everything that varies per game, in one place: the Type Object for <see cref="Game"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the facts below used to live in ten separate conditionals spread over
/// Domain, Infrastructure and the API, and seven of them were written as
/// <c>game == Game.Powerball ? x : y</c>. With exactly two games a two-way ternary is correct, so
/// nothing was wrong — but nothing was <em>checked</em> either. Adding a third game would have
/// compiled, passed, and silently served it Mega Millions' prize tiers, Mega Millions' dataset and
/// Mega Millions' parsing layout, because "not Powerball" and "Mega Millions" were the same
/// expression. No exception, no failing test, just wrong data.
/// </para>
/// <para>
/// A registry keyed by the enum turns that class of mistake into a lookup miss:
/// <see cref="For"/> throws for a game nobody registered, and adding a game means adding one
/// profile here rather than finding every conditional that happened to branch on it.
/// </para>
/// <para>
/// Only domain facts belong here. Per-game <em>feed</em> details (dataset ids, URL slugs, payload
/// layout) are an Infrastructure concern and live in that layer's own profile registry, so the
/// Domain does not learn the shape of anybody's HTTP API.
/// </para>
/// </remarks>
public sealed record GameProfile(
    Game Game,
    string Slug,
    IReadOnlyList<string> UrlAliases,
    string SpecialBallName,
    IReadOnlyList<DayOfWeek> DrawDays,
    TimeOnly DrawTimeEastern,
    IReadOnlyList<PrizeTier> PrizeTiers);

/// <summary>The registered <see cref="GameProfile"/> for every known game.</summary>
public static class GameProfiles
{
    // Prize tiers: NAMES are stable across history; amounts are current base values (Mega Millions
    // amounts since Apr 2025 are pre-multiplier minimums), so callers present them as approximate.
    private static readonly IReadOnlyList<PrizeTier> PowerballTiers =
    [
        new(5, true, "Match 5 + Powerball", null, true),
        new(5, false, "Match 5", 1_000_000m, false),
        new(4, true, "Match 4 + Powerball", 50_000m, false),
        new(4, false, "Match 4", 100m, false),
        new(3, true, "Match 3 + Powerball", 100m, false),
        new(3, false, "Match 3", 7m, false),
        new(2, true, "Match 2 + Powerball", 7m, false),
        new(1, true, "Match 1 + Powerball", 4m, false),
        new(0, true, "Match Powerball", 4m, false),
    ];

    private static readonly IReadOnlyList<PrizeTier> MegaMillionsTiers =
    [
        new(5, true, "Match 5 + Mega Ball", null, true),
        new(5, false, "Match 5", 1_000_000m, false),
        new(4, true, "Match 4 + Mega Ball", 10_000m, false),
        new(4, false, "Match 4", 500m, false),
        new(3, true, "Match 3 + Mega Ball", 200m, false),
        new(3, false, "Match 3", 10m, false),
        new(2, true, "Match 2 + Mega Ball", 10m, false),
        new(1, true, "Match 1 + Mega Ball", 7m, false),
        new(0, true, "Match Mega Ball", 5m, false),
    ];

    public static readonly IReadOnlyList<GameProfile> All =
    [
        new(
            Game.Powerball,
            Slug: "powerball",
            UrlAliases: ["powerball", "power-ball"],
            SpecialBallName: "Powerball",
            DrawDays: [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday],
            DrawTimeEastern: new TimeOnly(22, 59),
            PrizeTiers: PowerballTiers),
        new(
            Game.MegaMillions,
            Slug: "megamillions",
            UrlAliases: ["megamillions", "mega-millions"],
            SpecialBallName: "Mega Ball",
            DrawDays: [DayOfWeek.Tuesday, DayOfWeek.Friday],
            DrawTimeEastern: new TimeOnly(23, 0),
            PrizeTiers: MegaMillionsTiers),
    ];

    private static readonly FrozenDictionary<Game, GameProfile> ByGame =
        All.ToFrozenDictionary(p => p.Game);

    private static readonly FrozenDictionary<string, Game> ByAlias =
        All.SelectMany(p => p.UrlAliases.Select(a => (Alias: a, p.Game)))
           .ToFrozenDictionary(x => x.Alias, x => x.Game, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The profile for <paramref name="game"/>, or <see cref="ArgumentOutOfRangeException"/> for a
    /// game with no registered profile — the loud failure that a two-way ternary could not give.
    /// </summary>
    public static GameProfile For(Game game) =>
        ByGame.TryGetValue(game, out var profile)
            ? profile
            : throw new ArgumentOutOfRangeException(nameof(game), game,
                $"No {nameof(GameProfile)} is registered for {game}. Add one to {nameof(GameProfiles)}.{nameof(All)}.");

    /// <summary>Resolves a URL segment (any registered alias, case-insensitive) to its game.</summary>
    public static bool TryParse(string? value, out Game game)
    {
        if (value is not null && ByAlias.TryGetValue(value, out game))
            return true;

        game = default;
        return false;
    }

    /// <summary>Canonical slugs, quoted, for "unknown game" messages — so the list cannot drift.</summary>
    public static string KnownSlugs => string.Join(" or ", All.Select(p => $"'{p.Slug}'"));
}
