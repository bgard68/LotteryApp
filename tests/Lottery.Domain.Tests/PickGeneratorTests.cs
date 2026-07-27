using Lottery.Domain;

namespace Lottery.Domain.Tests;

public class PickGeneratorTests
{
    private static readonly RuleEra Powerball = RuleEras.ForDate(Game.Powerball, new DateOnly(2026, 7, 27));
    private static readonly RuleEra MegaMillions = RuleEras.ForDate(Game.MegaMillions, new DateOnly(2026, 7, 27));

    [Fact]
    public void Generate_IsDeterministicWithSeededRandom()
    {
        var a = new RandomPickGenerator(new Random(42)).Generate(Powerball);
        var b = new RandomPickGenerator(new Random(42)).Generate(Powerball);
        Assert.Equal(a.WhiteBalls, b.WhiteBalls);
        Assert.Equal(a.Special, b.Special);
    }

    [Fact]
    public void Generate_AlwaysValidForEra()
    {
        var generator = new RandomPickGenerator(new Random(7));
        foreach (var era in new[] { Powerball, MegaMillions })
        {
            for (var i = 0; i < 500; i++)
            {
                var (whites, special) = generator.Generate(era);
                Assert.Equal(5, whites.Count);
                Assert.Equal(5, whites.Distinct().Count());
                Assert.Equal(whites.OrderBy(n => n), whites);
                Assert.All(whites, w => Assert.True(era.IsValidWhite(w)));
                Assert.True(era.IsValidSpecial(special));
            }
        }
    }

    [Fact]
    public void Generate_CoversFullRange()
    {
        // Over many draws every value should appear - guards off-by-one at both ends.
        var generator = new RandomPickGenerator(new Random(1));
        var whitesSeen = new HashSet<int>();
        var specialsSeen = new HashSet<int>();
        for (var i = 0; i < 5000; i++)
        {
            var (whites, special) = generator.Generate(Powerball);
            whitesSeen.UnionWith(whites);
            specialsSeen.Add(special);
        }

        Assert.Equal(Enumerable.Range(1, 69), whitesSeen.Order());
        Assert.Equal(Enumerable.Range(1, 26), specialsSeen.Order());
    }
}
