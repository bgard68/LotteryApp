using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

public class RefreshGameTests
{
    // Monday 2026-07-27 23:30 ET (03:30 UTC Tue) - just after the Monday PB drawing.
    private static readonly FakeTimeProvider AfterMondayDraw = new(new DateTimeOffset(2026, 7, 28, 3, 30, 0, TimeSpan.Zero));

    private static readonly Draw Saturday = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [3, 4, 24, 36, 47], 17);
    private static readonly Draw Monday = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 27), [7, 19, 33, 51, 64], 18);

    private static FakeDrawRepository RepoWith(params Draw[] draws)
    {
        var repo = new FakeDrawRepository();
        repo.Draws.AddRange(draws);
        return repo;
    }

    [Fact]
    public async Task BehindSchedule_FetchesGapAndStoresNewDraws()
    {
        var repo = RepoWith(Saturday);
        var feed = new FakeNumbersFeed([Monday]);
        var refresh = new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.UpToDate);
        Assert.Equal(1, result.NewDraws);
        Assert.Equal(new DateOnly(2026, 7, 25), feed.LastRequestedAfter); // gap-repair asks from latest stored
        Assert.Equal(2, repo.Draws.Count);
    }

    [Fact]
    public async Task UpToDate_DoesNotCallNumbersFeed()
    {
        var repo = RepoWith(Saturday, Monday);
        var feed = new FakeNumbersFeed([]);
        var refresh = new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.UpToDate);
        Assert.Equal(0, result.NewDraws);
        Assert.Null(feed.LastRequestedAfter);
    }

    [Fact]
    public async Task FeedNotYetPublished_ReportsNotUpToDate()
    {
        var repo = RepoWith(Saturday);
        var refresh = new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(null),
            new FakeJackpotStore(), AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.False(result.UpToDate); // Monday drawing happened, feed has nothing yet
        Assert.Equal(0, result.NewDraws);
        Assert.Null(result.FeedError);
    }

    [Fact]
    public async Task FeedFailure_IsReportedNotThrown()
    {
        var repo = RepoWith(Saturday);
        var feed = new FakeNumbersFeed([Monday]) { ThrowOnFetch = new HttpRequestException("boom") };
        var refresh = new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.False(result.UpToDate);
        Assert.Equal("boom", result.FeedError);
        Assert.Single(repo.Draws);
    }

    [Fact]
    public async Task EraInvalidFeedDraw_IsSkippedNotStored()
    {
        var repo = RepoWith(Saturday);
        var badDraw = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 27), [7, 19, 33, 51, 70], 18); // 70 > 69
        var refresh = new RefreshGame(repo, new FakeNumbersFeed([badDraw]), new FakeJackpotFeed(null),
            new FakeJackpotStore(), AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(1, result.SkippedInvalid);
        Assert.Equal(0, result.NewDraws);
        Assert.Single(repo.Draws);
    }

    [Fact]
    public async Task JackpotInfo_SavesEstimateAndUpdatesDraw()
    {
        var mmDraw = Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 24), [2, 5, 42, 44, 60], 1);
        var repo = RepoWith(mmDraw);
        var store = new FakeJackpotStore();
        var info = new JackpotInfo(Game.MegaMillions, new DateOnly(2026, 7, 24),
            743_000_000m, false, 800_000_000m, 344_200_000m);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var refresh = new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(info), store, time);
        var result = await refresh.ExecuteAsync(Game.MegaMillions, CancellationToken.None);

        Assert.True(result.JackpotUpdated);
        Assert.Equal(800_000_000m, store.Saved!.NextEstimatedJackpot);
        var updated = repo.Draws.Single(d => d.Game == Game.MegaMillions);
        Assert.Equal(743_000_000m, updated.JackpotAmount);
        Assert.False(updated.JackpotWon);
    }
}
