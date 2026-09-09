using System.Globalization;
using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// DisplayAmount is the one piece of presentation the domain owns. The rule it
/// encodes is a domain rule rather than a formatting preference: a jackpot tier
/// pays an amount that is not known until the drawing, so it must never be
/// rendered as a fixed figure.
/// </summary>
public class PrizeTierTests
{
    [Fact]
    public void JackpotTier_NeverShowsAnAmount_EvenWhenOneIsSet()
    {
        var fromTable = PrizeTiers.For(Game.Powerball).Single(t => t.IsJackpot);
        // An amount alongside IsJackpot must lose to the jackpot flag, otherwise a
        // stale figure would be presented as the prize.
        var withAmount = fromTable with { ApproximateAmount = 1_000_000_000m };

        Assert.Equal("Jackpot", fromTable.DisplayAmount);
        Assert.Equal("Jackpot", withAmount.DisplayAmount);
    }

    [Fact]
    public void FixedTier_ShowsWholeDollarsWithNoCents()
    {
        var matchFive = PrizeTiers.For(Game.Powerball).Single(t => t.Name == "Match 5");

        // The format is culture-sensitive, so the culture is pinned here rather
        // than inherited from whatever the test host happens to be running under.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            Assert.Equal("$1,000,000", matchFive.DisplayAmount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TierWithNoKnownAmount_ShowsAPlaceholder()
    {
        // Every tier in the shipped tables has an amount; a future tier added
        // without one must degrade to a dash rather than to an empty cell.
        var unknown = new PrizeTier(2, false, "Match 2", ApproximateAmount: null, IsJackpot: false);

        Assert.Equal("-", unknown.DisplayAmount);
    }
}
