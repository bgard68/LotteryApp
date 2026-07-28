using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

/// <summary>
/// Count bounds were previously asserted only over HTTP by the smoke test.
/// They are a use-case rule, so they belong in a unit test too - the endpoint
/// is one caller, not the only possible one.
/// </summary>
public class GeneratePicksTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static GeneratePicks Subject(int seed = 12345) =>
        new(new RandomPickGenerator(new Random(seed)), new FakeTimeProvider(Now));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void InRangeCountsProduceThatManyTickets(int count)
    {
        var picks = Subject().Execute(Game.Powerball, count);

        Assert.Equal(count, picks.Tickets.Count);
        Assert.Equal(Game.Powerball, picks.Game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OutOfRangeCountsThrow(int count)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Subject().Execute(Game.Powerball, count));

        Assert.Contains($"between {GeneratePicks.MinCount} and {GeneratePicks.MaxCount}", ex.Message);
    }

    [Fact]
    public void DefaultCountIsOne()
    {
        Assert.Single(Subject().Execute(Game.Powerball).Tickets);
    }

    [Fact]
    public void BoundsAreTheAdvertisedContract()
    {
        Assert.Equal(1, GeneratePicks.MinCount);
        Assert.Equal(10, GeneratePicks.MaxCount);
    }

    [Theory]
    [InlineData(Game.Powerball)]
    [InlineData(Game.MegaMillions)]
    public void EveryTicketIsValidForTheCurrentEra(Game game)
    {
        var era = RuleEras.Current(game, DateOnly.FromDateTime(Now.UtcDateTime));

        var picks = Subject().Execute(game, GeneratePicks.MaxCount);

        Assert.All(picks.Tickets, t =>
        {
            Assert.Equal(RuleEra.WhiteBallCount, t.WhiteBalls.Count);
            // Distinct whites - a duplicate is not a playable ticket.
            Assert.Equal(t.WhiteBalls.Count, t.WhiteBalls.Distinct().Count());
            Assert.All(t.WhiteBalls, w => Assert.InRange(w, 1, era.WhiteBallMax));
            Assert.InRange(t.Special, 1, era.SpecialBallMax);
        });
    }

    [Fact]
    public void TicketsInOneBatchAreGeneratedIndependently()
    {
        // Ten identical tickets would mean the generator was reused wrongly.
        var picks = Subject().Execute(Game.Powerball, 10);

        var distinct = picks.Tickets
            .Select(t => string.Join(",", t.WhiteBalls) + "|" + t.Special)
            .Distinct()
            .Count();

        Assert.True(distinct > 1, "a batch of ten tickets should not be all identical");
    }
}
