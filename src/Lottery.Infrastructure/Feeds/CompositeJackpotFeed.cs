using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>Routes each game to its source-specific jackpot adapter.</summary>
public sealed class CompositeJackpotFeed(
    PowerballJackpotFeed powerball,
    MegaMillionsJackpotFeed megaMillions) : IJackpotFeed
{
    public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) => game switch
    {
        Game.Powerball => powerball.GetJackpotAsync(game, ct),
        Game.MegaMillions => megaMillions.GetJackpotAsync(game, ct),
        _ => Task.FromResult<JackpotInfo?>(null),
    };
}
