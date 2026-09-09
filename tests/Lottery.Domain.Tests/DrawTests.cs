using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// Draw hand-writes Equals/GetHashCode because a record compares its white-ball
/// list by reference, which would make two identical drawings unequal. Dedupe
/// on import, upsert idempotency and every Assert.Equal over draws rest on the
/// value semantics pinned here - as does Create refusing to build a drawing
/// that could not have happened.
/// </summary>
public class DrawTests
{
    private static readonly DateOnly Saturday = new(2026, 7, 25);

    private static Draw Sample() => Draw.Create(Game.Powerball, Saturday, [7, 19, 33, 51, 64], 18);

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 7, 19, 33, 51 })]
    [InlineData(new[] { 7, 19, 33, 51, 64, 65 })]
    public void Create_RejectsAnythingOtherThanFiveWhiteBalls(int[] whites)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Draw.Create(Game.Powerball, Saturday, whites, 18));

        Assert.Equal("whiteBalls", ex.ParamName);
        Assert.Contains($"got {whites.Length}", ex.Message);
    }

    [Fact]
    public void Create_RejectsRepeatedWhiteBalls()
    {
        // Five values but four distinct balls: a drawing that cannot physically
        // occur, and one that would inflate every later match count.
        var ex = Assert.Throws<ArgumentException>(() =>
            Draw.Create(Game.Powerball, Saturday, [7, 7, 33, 51, 64], 18));

        Assert.Equal("whiteBalls", ex.ParamName);
        Assert.Contains("distinct", ex.Message);
    }

    [Fact]
    public void Create_StoresWhiteBallsSortedRegardlessOfDrawOrder()
    {
        var draw = Draw.Create(Game.Powerball, Saturday, [64, 7, 51, 19, 33], 18);

        Assert.Equal([7, 19, 33, 51, 64], draw.WhiteBalls);
    }

    [Fact]
    public void SameNumbersDrawnInAnyOrder_AreOneAndTheSameDraw()
    {
        // The white-ball lists are distinct instances holding equal values, which
        // is exactly the case default record equality gets wrong.
        var a = Draw.Create(Game.Powerball, Saturday, [7, 19, 33, 51, 64], 18);
        var b = Draw.Create(Game.Powerball, Saturday, [64, 51, 33, 19, 7], 18);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        // Equal hash codes are what let a HashSet (and the import dedupe) see one draw.
        Assert.Single(new HashSet<Draw> { a, b });
    }

    [Fact]
    public void DrawsDifferingInAnyOneField_AreNotEqual()
    {
        // A hand-written Equals that quietly drops a field is the classic bug
        // here: each case below fails only if that field stopped being compared.
        var sample = Sample();
        (string Field, Draw Draw)[] variants =
        [
            ("game", sample with { Game = Game.MegaMillions }),
            ("draw date", sample with { DrawDate = Saturday.AddDays(-1) }),
            ("one white ball", sample with { WhiteBalls = [7, 19, 33, 51, 65] }),
            ("special ball", sample with { Special = 19 }),
            ("jackpot amount", sample with { JackpotAmount = 214_000_000m }),
            ("jackpot won", sample with { JackpotWon = true }),
        ];

        Assert.All(variants, v =>
        {
            Assert.False(sample.Equals(v.Draw), $"draws differing in {v.Field} must not be equal");
            Assert.NotEqual(sample, v.Draw);
        });
    }

    [Fact]
    public void ADrawIsNeverEqualToNull()
    {
        var draw = Sample();

        Assert.False(draw.Equals(null));
        Assert.False(draw == null);
    }
}
