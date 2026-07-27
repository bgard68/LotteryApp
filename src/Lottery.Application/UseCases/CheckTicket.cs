using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public enum CheckStatus
{
    Ok,
    InvalidTicket,

    /// <summary>No history imported - distinct from "checked and found nothing".</summary>
    DataUnavailable,
}

public sealed record TicketMatch(DateOnly DrawDate, int WhiteMatches, bool SpecialMatched,
    IReadOnlyList<int> DrawnWhiteBalls, int DrawnSpecial,
    string TierName, decimal? ApproximateAmount, bool IsJackpot);

public sealed record CheckResult(
    CheckStatus Status,
    string? Error,
    int DrawsChecked,
    DateOnly? HistorySince,
    IReadOnlyList<TicketMatch> Matches);

public sealed class CheckTicket(IDrawRepository draws, TimeProvider time)
{
    public async Task<CheckResult> ExecuteAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct)
    {
        // Validate against the CURRENT era - user picks are for future play.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var era = RuleEras.Current(game, today);

        if (whites.Count != RuleEra.WhiteBallCount)
            return Invalid($"Exactly {RuleEra.WhiteBallCount} white balls are required.");
        if (whites.Distinct().Count() != whites.Count)
            return Invalid("White balls must be distinct.");
        if (whites.Any(w => !era.IsValidWhite(w)))
            return Invalid($"White balls must be between 1 and {era.WhiteBallMax}.");
        if (!era.IsValidSpecial(special))
            return Invalid($"{game.SpecialBallName()} must be between 1 and {era.SpecialBallMax}.");

        var total = await draws.CountAsync(game, ct);
        if (total == 0)
            return new CheckResult(CheckStatus.DataUnavailable,
                "Winning-number history is not available right now.", 0, null, []);

        var earliest = await draws.EarliestDrawDateAsync(game, ct);
        var rows = await draws.FindMatchesAsync(game, whites, special, ct);

        var matches = rows
            .Select(r => (Row: r, Tier: PrizeTiers.TierFor(game, new MatchResult(r.DrawDate, r.WhiteMatches, r.SpecialMatched))))
            .Where(x => x.Tier is not null)
            .OrderByDescending(x => x.Row.DrawDate)
            .Select(x => new TicketMatch(x.Row.DrawDate, x.Row.WhiteMatches, x.Row.SpecialMatched,
                x.Row.DrawnWhiteBalls, x.Row.DrawnSpecial,
                x.Tier!.Name, x.Tier.ApproximateAmount, x.Tier.IsJackpot))
            .ToList();

        return new CheckResult(CheckStatus.Ok, null, total, earliest, matches);
    }

    private static CheckResult Invalid(string message) =>
        new(CheckStatus.InvalidTicket, message, 0, null, []);
}
