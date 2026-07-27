using Lottery.Domain;

namespace Lottery.Application.Abstractions;

public sealed record JackpotEstimate(
    Game Game,
    decimal? NextEstimatedJackpot,
    decimal? NextCashValue,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Persisted latest jackpot estimate per game, written by the refresh cycle.</summary>
public interface IJackpotStore
{
    Task<JackpotEstimate?> GetAsync(Game game, CancellationToken ct);
    Task SaveAsync(JackpotEstimate estimate, CancellationToken ct);
}
