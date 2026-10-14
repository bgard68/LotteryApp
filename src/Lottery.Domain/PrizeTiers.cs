namespace Lottery.Domain;

public sealed record PrizeTier(int WhiteMatches, bool SpecialMatched, string Name, decimal? ApproximateAmount, bool IsJackpot)
{
    /// <summary>Null amount = jackpot (varies); amounts are current-era base values, not historical.</summary>
    public string DisplayAmount => IsJackpot ? "Jackpot" : ApproximateAmount?.ToString("C0") ?? "-";
}

/// <summary>
/// Current-era prize structure per game. The tables themselves live on each
/// <see cref="GameProfile"/>; this is the lookup callers use.
/// </summary>
/// <remarks>
/// <see cref="For"/> was <c>game == Game.Powerball ? Powerball : MegaMillions</c>. That is correct
/// for two games and silently wrong for three — a new game would have been paid out against Mega
/// Millions' table, with nothing raised. Going through the registry makes an unregistered game
/// throw instead.
/// </remarks>
public static class PrizeTiers
{
    public static IReadOnlyList<PrizeTier> For(Game game) => GameProfiles.For(game).PrizeTiers;

    /// <summary>The tier a match result lands in, or null if it wins nothing.</summary>
    public static PrizeTier? TierFor(Game game, MatchResult result) =>
        For(game).FirstOrDefault(t => t.WhiteMatches == result.WhiteMatches && t.SpecialMatched == result.SpecialMatched);
}
