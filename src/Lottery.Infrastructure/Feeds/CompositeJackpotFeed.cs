using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.Extensions.Logging;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// Asks each registered jackpot source in turn and takes the first that answers — Chain of
/// Responsibility over <see cref="IJackpotFeed"/>.
/// </summary>
/// <remarks>
/// <para>
/// Routing lives in the sources, not here. Every adapter already declines the games it does not
/// serve by returning null, so "which source handles which game" is a fact each source states
/// about itself and this class does not need to know. Priority is registration order.
/// </para>
/// <para>
/// It previously took the three adapters as <em>concrete</em> constructor parameters and branched
/// on the game to pick between them — so the one class whose entire job is composition was the
/// one class coupled to every implementation. Adding or reordering a source meant editing its
/// constructor and its routing method, and it could not be tested against a stub without
/// instantiating three real adapters and three <see cref="HttpClient"/>s. Depending on the
/// abstraction it composes makes adding a source a registration change and nothing else.
/// </para>
/// </remarks>
public sealed class CompositeJackpotFeed(
    IEnumerable<IJackpotFeed> sources,
    ILogger<CompositeJackpotFeed> logger) : IJackpotFeed
{
    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        foreach (var source in sources)
        {
            var info = await source.GetJackpotAsync(game, ct);
            if (info is not null)
                return info;
        }

        logger.LogDebug("{Game}: no jackpot source produced a figure.", game);
        return null;
    }
}
