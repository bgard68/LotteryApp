using Lottery.Application.UseCases;
using Lottery.Domain;

namespace Lottery.Application.Tests;

/// <summary>
/// The limit is the only user-supplied number that reaches a query, so the
/// clamp is what stops "?limit=1000000" from becoming a table scan - and what
/// stops "?limit=0" or a negative from reaching SQL at all.
/// </summary>
public class GetDrawsTests
{
    private static FakeDrawRepository RepoWith(int drawCount)
    {
        var repo = new FakeDrawRepository();
        for (var i = 0; i < drawCount; i++)
            repo.Draws.Add(Draw.Create(Game.Powerball, new DateOnly(2020, 1, 1).AddDays(i), [1, 2, 3, 4, 5], 6));
        return repo;
    }

    [Theory]
    [InlineData(null, 50)]      // no limit supplied -> the default page
    [InlineData(0, 1)]          // zero would return nothing useful
    [InlineData(-5, 1)]         // negative must never reach the query
    [InlineData(1, 1)]
    [InlineData(200, 200)]      // exactly the ceiling
    [InlineData(201, 200)]      // one over
    [InlineData(1_000_000, 200)]
    public async Task LimitIsClampedIntoRange(int? requested, int expected)
    {
        var repo = RepoWith(300);

        var draws = await new GetDraws(repo).ExecuteAsync(
            Game.Powerball, from: null, to: null, requested, CancellationToken.None);

        Assert.Equal(expected, draws.Count);
    }

    [Fact]
    public async Task MaxLimitIsTheAdvertisedCeiling()
    {
        // The constant is part of the API contract (the endpoint documents it),
        // so a change here should be deliberate rather than incidental.
        Assert.Equal(200, GetDraws.MaxLimit);

        var draws = await new GetDraws(RepoWith(500)).ExecuteAsync(
            Game.Powerball, null, null, int.MaxValue, CancellationToken.None);

        Assert.Equal(GetDraws.MaxLimit, draws.Count);
    }

    [Fact]
    public async Task DateRangeFiltersInclusively()
    {
        var repo = RepoWith(10); // 2020-01-01 .. 2020-01-10

        var draws = await new GetDraws(repo).ExecuteAsync(
            Game.Powerball, new DateOnly(2020, 1, 3), new DateOnly(2020, 1, 5), null, CancellationToken.None);

        Assert.Equal(3, draws.Count);
        Assert.All(draws, d => Assert.InRange(d.DrawDate, new DateOnly(2020, 1, 3), new DateOnly(2020, 1, 5)));
    }

    [Fact]
    public async Task OtherGamesAreNeverReturned()
    {
        var repo = RepoWith(5);
        repo.Draws.Add(Draw.Create(Game.MegaMillions, new DateOnly(2020, 1, 2), [1, 2, 3, 4, 5], 6));

        var draws = await new GetDraws(repo).ExecuteAsync(
            Game.Powerball, null, null, null, CancellationToken.None);

        Assert.All(draws, d => Assert.Equal(Game.Powerball, d.Game));
    }

    [Fact]
    public async Task EmptyRepositoryReturnsEmptyNotNull()
    {
        var draws = await new GetDraws(new FakeDrawRepository()).ExecuteAsync(
            Game.Powerball, null, null, null, CancellationToken.None);

        Assert.Empty(draws);
    }
}
