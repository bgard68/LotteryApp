using Lottery.Domain;
using Lottery.Infrastructure.Feeds;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The routing/fallback policy: Mega Millions comes only from megamillions.com,
/// Powerball prefers the NY Lottery API and drops back to the retired
/// powerball.com endpoint. What matters is not just the answer but which sources
/// get called - a fallback that fires when the primary succeeded is a wasted
/// round trip against a rate-limited public endpoint, and one that never fires
/// is a Powerball jackpot silently missing from the app.
/// </summary>
public class CompositeJackpotFeedTests
{
    private const string NyBody =
        """{"data":{"draws":[{"drawTime":1785124800000,"estimatedJackpot":633000000,"jackpots":[{"amount":633000000,"cashAmount":277300000}]}]}}""";

    private const string PowerballBody =
        """[{"field_prize_amount":"$500 Million","field_prize_amount_cash":"$220 Million"}]""";

    private const string MegaMillionsBody =
        """<string xmlns="http://tempuri.org/">{"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"NextPrizePool":800000000.0,"NextCashValue":344200000.0,"Winners":0}}</string>""";

    private const string Unusable = """{"unexpected":true}""";

    private sealed record Harness(
        CompositeJackpotFeed Feed, StubHandler Ny, StubHandler Powerball, StubHandler MegaMillions);

    private static Harness Build(string ny, string powerball, string megaMillions)
    {
        var nyHandler = new StubHandler(ny);
        var pbHandler = new StubHandler(powerball);
        var mmHandler = new StubHandler(megaMillions);

        return new Harness(
            new CompositeJackpotFeed(
                new NyLotteryJackpotFeed(new HttpClient(nyHandler)),
                new PowerballJackpotFeed(new HttpClient(pbHandler)),
                new MegaMillionsJackpotFeed(new HttpClient(mmHandler))),
            nyHandler, pbHandler, mmHandler);
    }

    [Fact]
    public async Task MegaMillions_ComesFromMegaMillionsOnly()
    {
        var h = Build(ny: NyBody, powerball: PowerballBody, megaMillions: MegaMillionsBody);

        var info = await h.Feed.GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Equal(800_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(344_200_000m, info.NextCashValue);
        Assert.Equal(743_000_000m, info.LastJackpot); // the richest payload: only this source has it
        Assert.Equal(1, h.MegaMillions.Calls);
        Assert.Equal(0, h.Ny.Calls);
        Assert.Equal(0, h.Powerball.Calls);
    }

    [Fact]
    public async Task MegaMillions_WhenItsOnlySourceFails_IsNull()
    {
        // No fallback exists for Mega Millions; NY Lottery must not be tried.
        var h = Build(ny: NyBody, powerball: PowerballBody,
            megaMillions: """<string xmlns="http://tempuri.org/">{"Drawing":{}}</string>""");

        Assert.Null(await h.Feed.GetJackpotAsync(Game.MegaMillions, CancellationToken.None));
        Assert.Equal(0, h.Ny.Calls);
        Assert.Equal(0, h.Powerball.Calls);
    }

    [Fact]
    public async Task Powerball_PrefersNyLottery_AndSkipsTheFallback()
    {
        var h = Build(ny: NyBody, powerball: PowerballBody, megaMillions: MegaMillionsBody);

        var info = await h.Feed.GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(Game.Powerball, info!.Game);
        Assert.Equal(633_000_000m, info.NextEstimatedJackpot); // NY's figure, not powerball.com's
        Assert.Equal(277_300_000m, info.NextCashValue);
        Assert.Equal(1, h.Ny.Calls);
        Assert.Equal(0, h.Powerball.Calls);
        Assert.Equal(0, h.MegaMillions.Calls);
    }

    [Fact]
    public async Task Powerball_FallsBackToPowerballWhenNyLotteryHasNothing()
    {
        var h = Build(ny: Unusable, powerball: PowerballBody, megaMillions: MegaMillionsBody);

        var info = await h.Feed.GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.Equal(500_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(220_000_000m, info.NextCashValue);
        Assert.Equal(1, h.Ny.Calls);
        Assert.Equal(1, h.Powerball.Calls);
    }

    [Fact]
    public async Task Powerball_WhenBothSourcesAreUnusable_IsNull()
    {
        // The documented end state: numbers and countdowns still render, without amounts.
        var h = Build(ny: Unusable, powerball: "<!DOCTYPE html><html></html>", megaMillions: MegaMillionsBody);

        Assert.Null(await h.Feed.GetJackpotAsync(Game.Powerball, CancellationToken.None));
        Assert.Equal(1, h.Ny.Calls);
        Assert.Equal(1, h.Powerball.Calls);
        Assert.Equal(0, h.MegaMillions.Calls);
    }
}
