using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// Single-draw validation paths: the existing planted-violation test proves the
/// history sweep catches bad rows; these pin the per-draw contract - which era
/// is consulted, and what the violation actually says (the message is what a
/// human sees in CI when a real rule change lands).
/// </summary>
public class EraValidatorTests
{
    [Fact]
    public void Validate_EraValidDraw_ReturnsNull()
    {
        var draw = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [7, 19, 33, 51, 64], 18);

        Assert.Null(EraValidator.Validate(draw));
    }

    [Fact]
    public void Validate_WhiteAboveEraMax_ReportsBallsAndEra()
    {
        var draw = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 22), [7, 19, 33, 51, 70], 18);

        var violation = EraValidator.Validate(draw);

        Assert.NotNull(violation);
        Assert.Equal(Game.Powerball, violation!.Game);
        Assert.Equal(new DateOnly(2026, 7, 22), violation.DrawDate);
        Assert.Contains("White balls [7,19,33,51,70] outside 1-69", violation.Reason);
        Assert.Contains("2015-10-07", violation.Reason); // the era in force
    }

    [Fact]
    public void Validate_SpecialAboveEraMax_ReportsValueAndBounds()
    {
        var draw = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 20), [7, 19, 33, 51, 64], 27);

        var violation = EraValidator.Validate(draw);

        Assert.NotNull(violation);
        Assert.Contains("Special ball 27 outside 1-26", violation!.Reason);
    }

    [Fact]
    public void Validate_UsesTheErasOfTheDrawDate_NotTodays()
    {
        // Special 30 is legal in the 2012 era (max 35) but far above today's 26:
        // a validator wrongly using the current era would flag it.
        var draw2013 = Draw.Create(Game.Powerball, new DateOnly(2013, 6, 1), [5, 12, 23, 40, 59], 30);

        Assert.Null(EraValidator.Validate(draw2013));
    }

    [Fact]
    public void Validate_FlagsNumbersLegalTodayButNotThen()
    {
        // White 60 fits today's 69-ball matrix but not 2013's 59-ball one:
        // a validator wrongly using the current era would let it pass.
        var draw2013 = Draw.Create(Game.Powerball, new DateOnly(2013, 6, 1), [5, 12, 23, 40, 60], 30);

        var violation = EraValidator.Validate(draw2013);

        Assert.NotNull(violation);
        Assert.Contains("outside 1-59", violation!.Reason);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 6, true)]
    [InlineData(new[] { 1, 2, 3, 4 }, 6, false)]        // too few whites
    [InlineData(new[] { 1, 2, 3, 4, 5, 6 }, 7, false)]  // too many whites
    [InlineData(new[] { 1, 2, 3, 4, 4 }, 6, false)]     // duplicate white
    [InlineData(new[] { 0, 2, 3, 4, 5 }, 6, false)]     // white below 1
    [InlineData(new[] { 1, 2, 3, 4, 70 }, 6, false)]    // white above era max
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 0, false)]     // special below 1
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 27, false)]    // special above era max
    public void IsValidDraw_EnforcesEveryRule(int[] whites, int special, bool expected)
    {
        var currentPowerballEra = RuleEras.ForDate(Game.Powerball, new DateOnly(2026, 7, 27)); // 5/69 + 1/26

        Assert.Equal(expected, currentPowerballEra.IsValidDraw(whites, special));
    }
}
