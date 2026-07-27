using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// Routes each game to its jackpot sources, first-success wins:
/// - Mega Millions: megamillions.com (richest payload: rollover + last jackpot).
/// - Powerball: NY Lottery API (primary), then the retired powerball.com
///   endpoint as a just-in-case fallback.
/// </summary>
public sealed class CompositeJackpotFeed(
    NyLotteryJackpotFeed nyLottery,
    PowerballJackpotFeed powerball,
    MegaMillionsJackpotFeed megaMillions) : IJackpotFeed
{
    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        if (game == Game.MegaMillions)
            return await megaMillions.GetJackpotAsync(game, ct);

        return await nyLottery.GetJackpotAsync(game, ct)
            ?? await powerball.GetJackpotAsync(game, ct);
    }
}
