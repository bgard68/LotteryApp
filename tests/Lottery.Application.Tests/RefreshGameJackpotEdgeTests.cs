using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

/// <summary>
/// The jackpot half of a refresh cycle is optional by design - every part of
/// the feed payload can be missing independently. These tests pin what gets
/// persisted for each partial shape, and that jackpot failures stay silent
/// (reported via JackpotUpdated, never via FeedError and never thrown).
/// </summary>
public class RefreshGameJackpotEdgeTests
{
    // Just after the Monday 2026-07-27 Powerball drawing (23:30 ET).
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
    public async Task JackpotFeedThrows_NumbersStillStored_FailureStaysSilent()
    {
        var repo = RepoWith(Saturday);
        var store = new FakeJackpotStore();
        var refresh = new RefreshGame(repo, new FakeNumbersFeed([Monday]),
            new ThrowingJackpotFeed(new HttpRequestException("jackpot source down")), store, AfterMondayDraw);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(1, result.NewDraws);
        Assert.True(result.UpToDate);
        Assert.False(result.JackpotUpdated);
        Assert.Null(result.FeedError); // jackpot failures are silent by design
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task EstimateOnlyInfo_SavesTheEstimate_WithoutTouchingDraws()
    {
        var repo = RepoWith(Saturday, Monday); // up to date: isolates the jackpot path
        var store = new FakeJackpotStore();
        var info = new JackpotInfo(Game.Powerball, LastDrawDate: null, LastJackpot: null,
            LastJackpotWon: null, NextEstimatedJackpot: 633_000_000m, NextCashValue: 277_300_000m);

        var result = await new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(info), store, AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.JackpotUpdated);
        Assert.Equal(633_000_000m, store.Saved!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, store.Saved.NextCashValue);
        Assert.Equal(AfterMondayDraw.GetUtcNow(), store.Saved.UpdatedAtUtc);
        Assert.All(repo.Draws, d => Assert.Null(d.JackpotAmount)); // no stamp without LastDrawDate
    }

    [Fact]
    public async Task LastDrawOnlyInfo_StampsTheDraw_WithoutSavingAnEstimate()
    {
        var repo = RepoWith(Saturday, Monday);
        var store = new FakeJackpotStore();
        var info = new JackpotInfo(Game.Powerball, LastDrawDate: new DateOnly(2026, 7, 27),
            LastJackpot: 389_000_000m, LastJackpotWon: false,
            NextEstimatedJackpot: null, NextCashValue: null);

        var result = await new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(info), store, AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.JackpotUpdated);
        Assert.Null(store.Saved); // nothing to estimate, nothing saved
        var stamped = repo.Draws.Single(d => d.DrawDate == new DateOnly(2026, 7, 27));
        Assert.Equal(389_000_000m, stamped.JackpotAmount);
        Assert.False(stamped.JackpotWon);
    }

    [Fact]
    public async Task AllNullInfo_ReportsUpdated_ButPersistsNothing()
    {
        var repo = RepoWith(Saturday, Monday);
        var store = new FakeJackpotStore();
        var info = new JackpotInfo(Game.Powerball, null, null, null, null, null);

        var result = await new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(info), store, AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.True(result.JackpotUpdated); // the feed answered, even if with nothing usable
        Assert.Null(store.Saved);
        Assert.All(repo.Draws, d => Assert.Null(d.JackpotAmount));
    }

    [Fact]
    public async Task EmptyRepository_GapRepairsFromTheBeginningOfTime()
    {
        var repo = RepoWith();
        var feed = new FakeNumbersFeed([Saturday, Monday]);

        var result = await new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(DateOnly.MinValue, feed.LastRequestedAfter);
        Assert.Equal(2, result.NewDraws);
        Assert.True(result.UpToDate);
    }

    [Fact]
    public async Task NumbersFeedTimeout_IsReportedAsFeedError()
    {
        var repo = RepoWith(Saturday);
        var feed = new FakeNumbersFeed([Monday]) { ThrowOnFetch = new TaskCanceledException("request timed out") };

        var result = await new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal("request timed out", result.FeedError);
        Assert.False(result.UpToDate);
        Assert.Equal(0, result.NewDraws);
    }

    private sealed class ThrowingJackpotFeed(Exception exception) : IJackpotFeed
    {
        public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) => throw exception;
    }
}
