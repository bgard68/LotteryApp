using Lottery.Domain;
using Lottery.Infrastructure.Feeds;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Source-routing contract: Mega Millions goes straight to megamillions.com,
/// Powerball tries the NY Lottery API first and falls back to the retired
/// powerball.com endpoint - and a source that isn't needed is never called.
/// </summary>
public class CompositeJackpotFeedTests
{
    private static CompositeJackpotFeed Composite(
        RecordingHandler nyHandler, RecordingHandler powerballHandler, RecordingHandler megaMillionsHandler) =>
        new(new NyLotteryJackpotFeed(new HttpClient(nyHandler)),
            new PowerballJackpotFeed(new HttpClient(powerballHandler)),
            new MegaMillionsJackpotFeed(new HttpClient(megaMillionsHandler)));

    [Fact]
    public async Task MegaMillions_UsesOnlyTheMegaMillionsSource()
    {
        var ny = new RecordingHandler(FeedFixtures.NyLotteryPowerball);
        var pb = new RecordingHandler(FeedFixtures.PowerballComJson);
        var mm = new RecordingHandler(FeedFixtures.MegaMillions);

        var info = await Composite(ny, pb, mm).GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(800_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(344_200_000m, info.NextCashValue);
        Assert.Empty(ny.Requests);
        Assert.Empty(pb.Requests);
    }

    [Fact]
    public async Task Powerball_NySucceeds_FallbackIsNeverCalled()
    {
        var ny = new RecordingHandler(FeedFixtures.NyLotteryPowerball);
        var pb = new RecordingHandler(FeedFixtures.PowerballComJson);
        var mm = new RecordingHandler(FeedFixtures.MegaMillions);

        var info = await Composite(ny, pb, mm).GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Empty(pb.Requests); // first success wins, fallback untouched
        Assert.Empty(mm.Requests);
    }

    [Fact]
    public async Task Powerball_NyUnusable_FallsBackToPowerballCom()
    {
        var ny = new RecordingHandler("""{"unexpected":true}"""); // degrades to null
        var pb = new RecordingHandler(FeedFixtures.PowerballComJson);
        var mm = new RecordingHandler(FeedFixtures.MegaMillions);

        var info = await Composite(ny, pb, mm).GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, info.NextCashValue);
        Assert.Single(ny.Requests);
        Assert.Single(pb.Requests); // fallback actually consulted
    }

    [Fact]
    public async Task Powerball_EverySourceDead_ReturnsNull()
    {
        var ny = new RecordingHandler("""{"unexpected":true}""");
        var pb = new RecordingHandler("<!DOCTYPE html><html><head><title>Home | Powerball</title></head></html>");
        var mm = new RecordingHandler(FeedFixtures.MegaMillions);

        var info = await Composite(ny, pb, mm).GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.Null(info); // graceful degradation: no amounts rather than an error
    }
}
