using Lottery.Domain;

namespace Lottery.Domain.Tests;

public class TicketMatcherTests
{
    private static readonly Draw Sample = Draw.Create(
        Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);

    [Fact]
    public void Match_IsOrderIndependent()
    {
        var result = TicketMatcher.Match(Sample, [64, 7, 33, 19, 51], 18);
        Assert.Equal(5, result.WhiteMatches);
        Assert.True(result.SpecialMatched);
    }

    [Fact]
    public void SpecialBall_NeverCountsAsWhite()
    {
        // Ticket white 18 must not match the draw's special 18.
        var result = TicketMatcher.Match(Sample, [18, 1, 2, 3, 4], 5);
        Assert.Equal(0, result.WhiteMatches);
        Assert.False(result.SpecialMatched);
    }

    [Fact]
    public void WhiteBall_NeverCountsAsSpecial()
    {
        // Ticket special 7 must not match the draw's white 7.
        var result = TicketMatcher.Match(Sample, [1, 2, 3, 4, 5], 7);
        Assert.Equal(0, result.WhiteMatches);
        Assert.False(result.SpecialMatched);
    }

    [Theory]
    [InlineData(3, false, true)]  // Match 3 wins
    [InlineData(2, false, false)] // 2 whites alone wins nothing
    [InlineData(0, true, true)]   // special alone wins
    public void IsWinning_FollowsTierRules(int whites, bool special, bool expected)
    {
        var result = new MatchResult(new DateOnly(2026, 1, 1), whites, special);
        Assert.Equal(expected, result.IsWinning);
    }

    [Fact]
    public void PrizeTiers_MapEveryWinningCombination()
    {
        foreach (var game in Enum.GetValues<Game>())
        {
            Assert.Equal(9, PrizeTiers.For(game).Count);
            // Every winning MatchResult has a tier; every non-winning one has none.
            for (var w = 0; w <= 5; w++)
            {
                foreach (var s in new[] { true, false })
                {
                    var tier = PrizeTiers.TierFor(game, new MatchResult(new DateOnly(2026, 1, 1), w, s));
                    var winning = s || w >= 3;
                    Assert.Equal(winning, tier is not null);
                }
            }
        }
    }
}
