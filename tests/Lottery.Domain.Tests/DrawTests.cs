using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// Draw.Create is the only way rows enter the system from feeds and snapshots,
/// so its invariants (exactly 5 distinct whites, stored sorted) and the record's
/// value equality (used by repository round-trip tests) get direct coverage.
/// </summary>
public class DrawTests
{
    private static readonly DateOnly SaturdayDate = new(2026, 7, 25);

    [Fact]
    public void Create_UnsortedWhites_AreStoredSortedAscending()
    {
        var draw = Draw.Create(Game.Powerball, SaturdayDate, [64, 7, 51, 19, 33], 18);

        Assert.Equal([7, 19, 33, 51, 64], draw.WhiteBalls);
    }

    [Fact]
    public void Create_FourWhites_ThrowsWithCount()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Draw.Create(Game.Powerball, SaturdayDate, [1, 2, 3, 4], 18));

        Assert.Contains("Expected 5 white balls, got 4.", ex.Message);
        Assert.Equal("whiteBalls", ex.ParamName);
    }

    [Fact]
    public void Create_SixWhites_ThrowsWithCount()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Draw.Create(Game.Powerball, SaturdayDate, [1, 2, 3, 4, 5, 6], 18));

        Assert.Contains("Expected 5 white balls, got 6.", ex.Message);
    }

    [Fact]
    public void Create_DuplicateWhites_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 51], 18));

        Assert.Contains("White balls must be distinct.", ex.Message);
    }

    [Fact]
    public void Create_CarriesJackpotFacts()
    {
        var draw = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18,
            jackpotAmount: 389_000_000m, jackpotWon: false);

        Assert.Equal(389_000_000m, draw.JackpotAmount);
        Assert.False(draw.JackpotWon);
    }

    [Fact]
    public void Create_JackpotFactsDefaultToNull()
    {
        var draw = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18);

        Assert.Null(draw.JackpotAmount);
        Assert.Null(draw.JackpotWon);
    }

    [Fact]
    public void Equals_SameValuesInDifferentListInstances_AreEqual()
    {
        // Records compare collections by reference; Draw overrides to content -
        // repository round-trip assertions depend on this.
        var a = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18, 389_000_000m, false);
        var b = Draw.Create(Game.Powerball, SaturdayDate, [64, 51, 33, 19, 7], 18, 389_000_000m, false);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_OneDifferentWhiteBall_AreNotEqual()
    {
        var a = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18);
        var b = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 65], 18);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentSpecial_AreNotEqual()
    {
        var a = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18);
        var b = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 19);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentGameSameNumbers_AreNotEqual()
    {
        var a = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18);
        var b = Draw.Create(Game.MegaMillions, SaturdayDate, [7, 19, 33, 51, 64], 18);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentJackpotAmount_AreNotEqual()
    {
        var a = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18, 389_000_000m);
        var b = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18, 400_000_000m);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        var draw = Draw.Create(Game.Powerball, SaturdayDate, [7, 19, 33, 51, 64], 18);

        Assert.False(draw.Equals(null));
    }
}
