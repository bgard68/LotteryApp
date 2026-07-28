using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

/// <summary>
/// The frontend validates tickets against whatever this returns, so "exactly
/// one era is current" is not cosmetic - zero would disable validation and two
/// would make it ambiguous.
/// </summary>
public class GetRuleErasTests
{
    private static GetRuleEras At(int year, int month, int day) =>
        new(new FakeTimeProvider(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public void ExactlyOneEraIsCurrent(Game game)
    {
        var eras = At(2026, 7, 27).Execute(game);

        Assert.Single(eras, e => e.IsCurrent);
    }

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public void ErasAreOrderedAndOnlyForTheRequestedGame(Game game)
    {
        var eras = At(2026, 7, 27).Execute(game);

        Assert.NotEmpty(eras);
        Assert.Equal(eras.OrderBy(e => e.EffectiveFrom).ToList(), eras);
        Assert.All(eras, e =>
        {
            Assert.Equal(RuleEra.WhiteBallCount, e.WhiteBallCount);
            Assert.True(e.WhiteBallMax > 0);
            Assert.True(e.SpecialBallMax > 0);
        });
    }

    [Fact]
    public void TheCurrentEraIsTheLastOneThatHasStarted()
    {
        var eras = At(2026, 7, 27).Execute(Game.Powerball);
        var current = eras.Single(e => e.IsCurrent);

        Assert.Equal(eras[^1].EffectiveFrom, current.EffectiveFrom);
        // Powerball's 2015 era: 69 whites, 26 reds - still current in 2026.
        Assert.Equal(69, current.WhiteBallMax);
        Assert.Equal(26, current.SpecialBallMax);
    }

    [Fact]
    public void OnTheDayAnEraStarts_ThatEraIsAlreadyCurrent()
    {
        // Powerball moved to 69/26 on 2015-10-07; the boundary day itself must
        // use the new rules, not the previous era's.
        var dayBefore = At(2015, 10, 6).Execute(Game.Powerball).Single(e => e.IsCurrent);
        var firstDay = At(2015, 10, 7).Execute(Game.Powerball).Single(e => e.IsCurrent);

        Assert.NotEqual(dayBefore.EffectiveFrom, firstDay.EffectiveFrom);
        Assert.Equal(new DateOnly(2015, 10, 7), firstDay.EffectiveFrom);
        Assert.Equal(69, firstDay.WhiteBallMax);
    }

    [Fact]
    public void AtTheDawnOfHistory_TheEarliestEraIsCurrent()
    {
        // A clock set before any era began must still yield exactly one current
        // era rather than throwing or returning none.
        var eras = At(1992, 4, 22).Execute(Game.Powerball);

        Assert.Single(eras, e => e.IsCurrent);
    }
}
