using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Application.Tests;

public class RefreshGameTests
{
    // Monday 2026-07-27 23:30 ET (03:30 UTC Tue) - just after the Monday PB drawing.
    private static readonly FakeTimeProvider AfterMondayDraw = new(new DateTimeOffset(2026, 7, 28, 3, 30, 0, TimeSpan.Zero));

    // Saturday 2026-07-25 08:00 ET - after Friday night's Mega Millions drawing.
    private static readonly FakeTimeProvider AfterFridayMmDraw = new(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

    private static readonly Draw Saturday = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [3, 4, 24, 36, 47], 17);
    private static readonly Draw Monday = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 27), [7, 19, 33, 51, 64], 18);

    // Already carries jackpot facts, so a later refresh can only preserve or destroy them.
    private static readonly Draw MegaMillionsFriday = Draw.Create(
        Game.MegaMillions, new DateOnly(2026, 7, 24), [2, 5, 42, 44, 60], 1,
        jackpotAmount: 743_000_000m, jackpotWon: false);

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
    public async Task FirstRefreshOfAnEmptyDatabase_AsksTheFeedForEverything()
    {
        // Cold start: with no stored draw there is no "after" date to repair from,
        // so the feed must be asked from the beginning of time rather than from
        // today - otherwise a fresh deployment starts life permanently behind.
        var repo = new FakeDrawRepository();
        var feed = new FakeNumbersFeed([Saturday, Monday]);

        var result = await new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(DateOnly.MinValue, feed.LastRequestedAfter);
        Assert.Equal(2, result.NewDraws);
        Assert.True(result.UpToDate);
    }

    [Fact]
    public async Task EmptyDatabaseAndASilentFeed_ReportsNotUpToDate()
    {
        // The "is it current?" check runs against a database that is still empty;
        // it has to answer no rather than trip over the missing draw.
        var result = await new RefreshGame(new FakeDrawRepository(), new FakeNumbersFeed([]),
                new FakeJackpotFeed(null), new FakeJackpotStore(), AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.False(result.UpToDate);
        Assert.Equal(0, result.NewDraws);
        Assert.Null(result.FeedError);
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

    [Fact]
    public async Task JackpotFeedFailure_CostsOnlyTheJackpot()
    {
        // Jackpot data is optional by design: when that source throws, the
        // winning numbers half of the cycle must still complete, and the failure
        // must not be reported as a numbers-feed error either.
        var repo = RepoWith(Saturday);
        var store = new FakeJackpotStore();
        var jackpots = new FakeJackpotFeed(null) { ThrowOnFetch = new HttpRequestException("jackpot source down") };

        var result = await new RefreshGame(repo, new FakeNumbersFeed([Monday]), jackpots, store, AfterMondayDraw)
            .ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.False(result.JackpotUpdated);
        Assert.Null(result.FeedError);
        Assert.Equal(1, result.NewDraws);
        Assert.True(result.UpToDate);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task EmptyJackpotSnapshot_OverwritesNothing()
    {
        // Every field of JackpotInfo is optional. A snapshot that carries no
        // figures at all must leave the stored estimate and the stored draw as
        // they were rather than blanking them.
        var repo = RepoWith(MegaMillionsFriday);
        var store = new FakeJackpotStore();
        var previous = new JackpotEstimate(Game.MegaMillions, 800_000_000m, 344_200_000m, AfterFridayMmDraw.GetUtcNow());
        await store.SaveAsync(previous, CancellationToken.None);
        var empty = new JackpotInfo(Game.MegaMillions, null, null, null, null, null);

        var result = await new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(empty), store, AfterFridayMmDraw)
            .ExecuteAsync(Game.MegaMillions, CancellationToken.None);

        Assert.True(result.JackpotUpdated); // the source answered - there was simply nothing in it
        Assert.Equal(previous, store.Saved);
        Assert.Equal(743_000_000m, repo.Draws.Single(d => d.Game == Game.MegaMillions).JackpotAmount);
    }

    [Fact]
    public async Task JackpotSnapshotWithADateButNoAmount_LeavesTheStoredDrawAlone()
    {
        // Half a snapshot - the drawing's date, but no jackpot figure - must not
        // reach UpdateJackpotAsync, which would erase an amount already stored.
        var repo = RepoWith(MegaMillionsFriday);
        var store = new FakeJackpotStore();
        var partial = new JackpotInfo(Game.MegaMillions, new DateOnly(2026, 7, 24),
            LastJackpot: null, LastJackpotWon: null, NextEstimatedJackpot: 900_000_000m, NextCashValue: null);

        var result = await new RefreshGame(repo, new FakeNumbersFeed([]), new FakeJackpotFeed(partial), store, AfterFridayMmDraw)
            .ExecuteAsync(Game.MegaMillions, CancellationToken.None);

        Assert.True(result.JackpotUpdated);
        var stored = repo.Draws.Single(d => d.Game == Game.MegaMillions);
        Assert.Equal(743_000_000m, stored.JackpotAmount);
        Assert.False(stored.JackpotWon);
        // The estimate half of the same snapshot is still worth saving.
        Assert.Equal(900_000_000m, store.Saved!.NextEstimatedJackpot);
        Assert.Null(store.Saved.NextCashValue);
    }

    [Fact]
    public async Task RefreshingOneGame_IgnoresTheOtherGamesDraws()
    {
        // Powerball draws more often than Mega Millions. If the game ever stopped
        // being threaded through to the repository, gap-repair would ask the feed
        // for everything after Powerball's newer date and skip a Mega Millions
        // drawing entirely.
        var repo = RepoWith(Saturday, Monday, MegaMillionsFriday);
        var mmTuesday = Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 28), [1, 3, 5, 7, 9], 2);
        var feed = new FakeNumbersFeed([mmTuesday]);
        var afterTuesdayMmDraw = new FakeTimeProvider(new DateTimeOffset(2026, 7, 29, 3, 30, 0, TimeSpan.Zero));

        var result = await new RefreshGame(repo, feed, new FakeJackpotFeed(null), new FakeJackpotStore(), afterTuesdayMmDraw)
            .ExecuteAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Equal(Game.MegaMillions, result.Game);
        Assert.Equal(new DateOnly(2026, 7, 24), feed.LastRequestedAfter); // Mega Millions' own latest, not Powerball's
        Assert.Equal(1, result.NewDraws);
        Assert.True(result.UpToDate);
        Assert.Equal(2, repo.Draws.Count(d => d.Game == Game.Powerball));
    }

    [Fact]
    public async Task ADrawDatedBeforeAnyKnownEra_IsReportedAsAFeedError_NotThrown()
    {
        // EraValidator.Validate calls RuleEras.ForDate, which THROWS for a date
        // no era covers rather than returning a violation. That exception used
        // to escape ExecuteAsync entirely, and /internal/refresh - which has no
        // try/catch of its own - answered 500. This class documents that feed
        // failures are reported, never thrown.
        var repo = new FakeDrawRepository();
        var stale = Draw.Create(Game.Powerball, new DateOnly(1980, 1, 5), [1, 2, 3, 4, 5], 6);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

        var refresh = new RefreshGame(repo, new FakeNumbersFeed([stale]), new FakeJackpotFeed(null),
            new FakeJackpotStore(), time);

        var result = await refresh.ExecuteAsync(Game.Powerball, CancellationToken.None);

        Assert.NotNull(result.FeedError);
        Assert.Equal(0, result.NewDraws);
    }
}
