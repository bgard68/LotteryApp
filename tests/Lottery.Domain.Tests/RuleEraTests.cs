using Lottery.Domain;

namespace Lottery.Domain.Tests;

public class RuleEraTests
{
    private static readonly RuleEra CurrentPowerball =
        RuleEras.ForDate(Game.Powerball, new DateOnly(2026, 7, 27)); // 5/69 + 1/26

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

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]   // there is no ball zero
    [InlineData(1, true)]
    [InlineData(69, true)]   // top of the current 5/69 matrix
    [InlineData(70, false)]
    public void IsValidWhite_IncludesBothEndsOfTheMatrixAndNothingBeyond(int ball, bool expected)
    {
        Assert.Equal(expected, CurrentPowerball.IsValidWhite(ball));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(26, true)]   // top of the current 1/26 Powerball range
    [InlineData(27, false)]
    public void IsValidSpecial_IncludesBothEndsOfTheRangeAndNothingBeyond(int ball, bool expected)
    {
        Assert.Equal(expected, CurrentPowerball.IsValidSpecial(ball));
    }

    [Theory]
    [InlineData(new[] { 7, 19, 33, 51, 64 }, 18, true)]
    [InlineData(new[] { 7, 19, 33, 51 }, 18, false)]           // four whites
    [InlineData(new[] { 7, 19, 33, 51, 64, 65 }, 18, false)]   // six whites
    [InlineData(new[] { 7, 7, 33, 51, 64 }, 18, false)]        // five values, four balls
    [InlineData(new[] { 7, 19, 33, 51, 70 }, 18, false)]       // white above the matrix
    [InlineData(new[] { 0, 19, 33, 51, 64 }, 18, false)]       // white below the matrix
    [InlineData(new[] { 7, 19, 33, 51, 64 }, 27, false)]       // special above the range
    [InlineData(new[] { 7, 19, 33, 51, 64 }, 0, false)]        // special below the range
    public void IsValidDraw_RequiresFiveDistinctInRangeWhitesAndAnInRangeSpecial(
        int[] whites, int special, bool expected)
    {
        Assert.Equal(expected, CurrentPowerball.IsValidDraw(whites, special));
    }

    [Fact]
    public void EveryGamesErasAreOrderedAscendingAndDistinct()
    {
        // ForDate scans the table once and keeps the last era whose start date has
        // passed, which is only correct while each game's rows ascend; a row typed
        // in out of order would silently mis-validate every draw after it.
        foreach (var game in Enum.GetValues<Game>())
        {
            var eras = RuleEras.All.Where(e => e.Game == game).ToList();

            Assert.NotEmpty(eras);
            Assert.Equal(eras.OrderBy(e => e.EffectiveFrom), eras);
            Assert.Equal(eras.Count, eras.Select(e => e.EffectiveFrom).Distinct().Count());
        }
    }

    [Fact]
    public void CurrentEra_IsTheNewestEraOnRecordForTheGame()
    {
        // Nothing here schedules a matrix that has not started yet, so "current"
        // must be the last row for the game - if it is not, the table contains an
        // unreleased era and today's tickets are being validated against it.
        var today = new DateOnly(2026, 7, 27);

        foreach (var game in Enum.GetValues<Game>())
        {
            var newest = RuleEras.All.Where(e => e.Game == game).OrderBy(e => e.EffectiveFrom).Last();

            Assert.Equal(newest, RuleEras.Current(game, today));
        }
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
