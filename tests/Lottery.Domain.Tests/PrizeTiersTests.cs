using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// The tier tables are the payout contract shown to users. TicketMatcherTests
/// asserts every winning combination has *a* tier and PrizeTierTests covers
/// DisplayAmount; these pin each tier's exact name, amount, and jackpot flag,
/// so a fat-fingered edit to one row fails one specific case.
/// </summary>
public class PrizeTiersTests
{
    private static readonly DateOnly AnyDate = new(2026, 1, 1);

    [Theory]
    [InlineData(5, true, "Match 5 + Powerball", null, true)]
    [InlineData(5, false, "Match 5", 1_000_000L, false)]
    [InlineData(4, true, "Match 4 + Powerball", 50_000L, false)]
    [InlineData(4, false, "Match 4", 100L, false)]
    [InlineData(3, true, "Match 3 + Powerball", 100L, false)]
    [InlineData(3, false, "Match 3", 7L, false)]
    [InlineData(2, true, "Match 2 + Powerball", 7L, false)]
    [InlineData(1, true, "Match 1 + Powerball", 4L, false)]
    [InlineData(0, true, "Match Powerball", 4L, false)]
    public void TierFor_Powerball_MapsExactTier(int whites, bool special, string name, long? amount, bool isJackpot)
    {
        var result = new MatchResult(AnyDate, whites, special);

        var tier = PrizeTiers.TierFor(Game.Powerball, result);

        Assert.NotNull(tier);
        Assert.Equal(name, tier!.Name);
        Assert.Equal((decimal?)amount, tier.ApproximateAmount);
        Assert.Equal(isJackpot, tier.IsJackpot);
    }

    [Theory]
    [InlineData(5, true, "Match 5 + Mega Ball", null, true)]
    [InlineData(5, false, "Match 5", 1_000_000L, false)]
    [InlineData(4, true, "Match 4 + Mega Ball", 10_000L, false)]
    [InlineData(4, false, "Match 4", 500L, false)]
    [InlineData(3, true, "Match 3 + Mega Ball", 200L, false)]
    [InlineData(3, false, "Match 3", 10L, false)]
    [InlineData(2, true, "Match 2 + Mega Ball", 10L, false)]
    [InlineData(1, true, "Match 1 + Mega Ball", 7L, false)]
    [InlineData(0, true, "Match Mega Ball", 5L, false)]
    public void TierFor_MegaMillions_MapsExactTier(int whites, bool special, string name, long? amount, bool isJackpot)
    {
        var result = new MatchResult(AnyDate, whites, special);

        var tier = PrizeTiers.TierFor(Game.MegaMillions, result);

        Assert.NotNull(tier);
        Assert.Equal(name, tier!.Name);
        Assert.Equal((decimal?)amount, tier.ApproximateAmount);
        Assert.Equal(isJackpot, tier.IsJackpot);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void TierFor_NonWinningCombination_ReturnsNull(int whites, bool special)
    {
        var result = new MatchResult(AnyDate, whites, special);

        Assert.Null(PrizeTiers.TierFor(Game.Powerball, result));
        Assert.Null(PrizeTiers.TierFor(Game.MegaMillions, result));
    }

}
