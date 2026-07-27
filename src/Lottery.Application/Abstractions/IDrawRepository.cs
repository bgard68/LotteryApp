using Lottery.Domain;

namespace Lottery.Application.Abstractions;

/// <summary>Carries the drawn numbers so the UI can highlight which of the ticket's numbers hit.</summary>
public sealed record MatchRow(
    DateOnly DrawDate,
    IReadOnlyList<int> DrawnWhiteBalls,
    int DrawnSpecial,
    int WhiteMatches,
    bool SpecialMatched);

public interface IDrawRepository
{
    Task<Draw?> GetLatestAsync(Game game, CancellationToken ct);
    Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct);
    Task<int> CountAsync(Game game, CancellationToken ct);
    Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct);

    /// <summary>Rows with any white or special match, computed set-wise in SQL.</summary>
    Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct);

    /// <summary>Insert if absent; the unique (Game, DrawDate) index makes retries idempotent.</summary>
    Task<bool> UpsertAsync(Draw draw, CancellationToken ct);

    /// <summary>Bulk insert for the one-time historical import, single transaction.</summary>
    Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct);

    /// <summary>Attach jackpot facts (amount, won) to an already-stored draw.</summary>
    Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct);
}
