using Lottery.Domain;

namespace Lottery.Application.Abstractions;

/// <summary>
/// Jackpot data for a game. Everything is nullable by design: the sources are
/// undocumented official-site endpoints, so any part can be missing and the
/// system degrades to showing numbers without amounts.
/// </summary>
public sealed record JackpotInfo(
    Game Game,
    DateOnly? LastDrawDate,
    decimal? LastJackpot,
    bool? LastJackpotWon,
    decimal? NextEstimatedJackpot,
    decimal? NextCashValue);

public interface IJackpotFeed
{
    /// <summary>Current jackpot snapshot, or null when the source is unavailable.</summary>
    Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct);
}
