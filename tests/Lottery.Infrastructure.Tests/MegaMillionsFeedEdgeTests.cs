using Lottery.Domain;
using Lottery.Infrastructure.Feeds;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Defensive-parsing edges of the megamillions.com adapter beyond the base
/// happy-path fixture: winner-count semantics, wrong-game short-circuit, and
/// the degrade-to-null paths for partial payloads.
/// </summary>
public class MegaMillionsFeedEdgeTests
{
    private static string Wrapped(string json) =>
        $"""<?xml version="1.0" encoding="utf-8"?><string xmlns="http://tempuri.org/">{json}</string>""";

    [Fact]
    public async Task PositiveWinnerCount_MeansJackpotWasWon()
    {
        var handler = new RecordingHandler(Wrapped(
            """{"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"NextPrizePool":50000000.0,"NextCashValue":21500000.0,"Winners":2}}"""));

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.NotNull(info);
        Assert.True(info!.LastJackpotWon); // Winners: 2 -> won, not rolled over
        Assert.Equal(743_000_000m, info.LastJackpot);
    }

    [Fact]
    public async Task WrongGame_ReturnsNullWithoutCallingTheEndpoint()
    {
        var handler = new RecordingHandler(FeedFixtures.MegaMillions);

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.Powerball, CancellationToken.None);

        Assert.Null(info);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NonXmlBody_DegradesToNull()
    {
        // A bot challenge or outage page instead of the XML-wrapped payload
        // must yield null, not an XmlException escaping the refresh cycle.
        var handler = new RecordingHandler("<!DOCTYPE html><html><head><title>503</title></head></html>");

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task MalformedJsonInsideTheXml_DegradesToNull()
    {
        var handler = new RecordingHandler(Wrapped("{not json at all"));

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task EmptyXmlElement_DegradesToNull()
    {
        var handler = new RecordingHandler("""<string xmlns="http://tempuri.org/"></string>""");

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task PayloadWithoutJackpotNode_DegradesToNull()
    {
        var handler = new RecordingHandler(Wrapped("""{"Drawing":{"PlayDate":"2026-07-24T00:00:00"}}"""));

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task UnparseablePlayDate_NullsTheDateButKeepsTheAmounts()
    {
        var handler = new RecordingHandler(Wrapped(
            """{"Jackpot":{"PlayDate":"not-a-date","NextPrizePool":800000000.0,"NextCashValue":344200000.0,"Winners":0}}"""));

        var info = await new MegaMillionsJackpotFeed(new HttpClient(handler))
            .GetJackpotAsync(Game.MegaMillions, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Null(info!.LastDrawDate);
        Assert.Equal(800_000_000m, info.NextEstimatedJackpot);
        Assert.Equal(344_200_000m, info.NextCashValue);
    }
}
