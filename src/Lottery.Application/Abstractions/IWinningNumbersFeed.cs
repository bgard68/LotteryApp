using Lottery.Domain;

namespace Lottery.Application.Abstractions;

/// <summary>Live source of winning numbers, used for incremental refresh and gap-repair.</summary>
public interface IWinningNumbersFeed
{
    /// <summary>Draws strictly after the given date, ascending. Empty when the feed has nothing newer.</summary>
    Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct);
}
