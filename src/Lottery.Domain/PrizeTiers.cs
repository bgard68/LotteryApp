namespace Lottery.Domain;

public sealed record PrizeTier(int WhiteMatches, bool SpecialMatched, string Name, decimal? ApproximateAmount, bool IsJackpot)
{
    /// <summary>Null amount = jackpot (varies); amounts are current-era base values, not historical.</summary>
    public string DisplayAmount => IsJackpot ? "Jackpot" : ApproximateAmount?.ToString("C0") ?? "-";
}

/// <summary>
/// Current-era prize structure per game. Tier NAMES are stable across history;
/// amounts are current base values (Mega Millions amounts since Apr 2025 are
/// pre-multiplier minimums), so callers should present them as approximate.
/// </summary>
public static class PrizeTiers
{
    private static readonly IReadOnlyList<PrizeTier> Powerball =
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

    private static readonly IReadOnlyList<PrizeTier> MegaMillions =
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

    public static IReadOnlyList<PrizeTier> For(Game game) =>
        game == Game.Powerball ? Powerball : MegaMillions;

    /// <summary>The tier a match result lands in, or null if it wins nothing.</summary>
    public static PrizeTier? TierFor(Game game, MatchResult result) =>
        For(game).FirstOrDefault(t => t.WhiteMatches == result.WhiteMatches && t.SpecialMatched == result.SpecialMatched);
}
