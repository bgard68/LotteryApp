using Lottery.Domain;

namespace Lottery.Domain.Tests;

public class RuleEraTests
{
    [Theory]
    [InlineData(2015, 10, 6, 59, 35)]  // last day of the 5/59+1/35 era
    [InlineData(2015, 10, 7, 69, 26)]  // first draw of the current matrix
    [InlineData(2026, 7, 27, 69, 26)]
    public void Powerball_EraBoundaries(int y, int m, int d, int whiteMax, int specialMax)
    {
        var era = RuleEras.ForDate(Game.Powerball, new DateOnly(y, m, d));
        Assert.Equal(whiteMax, era.WhiteBallMax);
        Assert.Equal(specialMax, era.SpecialBallMax);
    }

    [Theory]
    [InlineData(2017, 10, 30, 75, 15)]
    [InlineData(2017, 10, 31, 70, 25)]
    [InlineData(2025, 4, 8, 70, 24)]   // April 2025 revamp
    public void MegaMillions_EraBoundaries(int y, int m, int d, int whiteMax, int specialMax)
    {
        var era = RuleEras.ForDate(Game.MegaMillions, new DateOnly(y, m, d));
        Assert.Equal(whiteMax, era.WhiteBallMax);
        Assert.Equal(specialMax, era.SpecialBallMax);
    }

    [Fact]
    public void DateBeforeFirstEra_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RuleEras.ForDate(Game.MegaMillions, new DateOnly(2000, 1, 1)));
    }

    [Fact]
    public void EraValidator_FlagsPlantedViolation()
    {
        // A 70 white ball planted into the 5/69 era must be detected - this is
        // the mechanism that turns an unknown future rule change into a loud failure.
        var good = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);
        var badWhite = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [7, 19, 33, 51, 70], 18);
        var badSpecial = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 20), [7, 19, 33, 51, 64], 27);

        var violations = EraValidator.ValidateHistory([good, badWhite, badSpecial]);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.DrawDate == new DateOnly(2026, 7, 22));
        Assert.Contains(violations, v => v.DrawDate == new DateOnly(2026, 7, 20));
    }
}
