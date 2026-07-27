namespace Lottery.Domain;

/// <summary>Outcome of comparing one ticket against one drawing.</summary>
public sealed record MatchResult(DateOnly DrawDate, int WhiteMatches, bool SpecialMatched)
{
    /// <summary>Any prize tier requires the special ball or at least 3 whites.</summary>
    public bool IsWinning => SpecialMatched || WhiteMatches >= 3;
}

/// <summary>
/// Order-independent matching: whites via set intersection, the special ball via
/// strict equality kept entirely separate from the white pool.
/// </summary>
public static class TicketMatcher
{
    public static MatchResult Match(Draw draw, IReadOnlyCollection<int> ticketWhites, int ticketSpecial)
    {
        var whiteMatches = draw.WhiteBalls.Intersect(ticketWhites).Count();
        var specialMatched = draw.Special == ticketSpecial;
        return new MatchResult(draw.DrawDate, whiteMatches, specialMatched);
    }
}
