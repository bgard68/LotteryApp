using Lottery.Infrastructure.Feeds;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Contract tests against recorded real payloads, so a silent shape change in
/// the undocumented feeds breaks a test instead of production.
/// </summary>
public class FeedParsingTests
{
    // Captured from megamillions.com GetLatestDrawData, 2026-07-27 (trimmed).
    private const string MmFixture =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <string xmlns="http://tempuri.org/">{"Drawing":{"PlayDate":"2026-07-24T00:00:00","N1":2,"N2":5,"N3":42,"N4":44,"N5":60,"MBall":1,"Megaplier":-1},"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"NextPrizePool":800000000.0,"CurrentCashValue":323400000.0,"NextCashValue":344200000.0,"Winners":0,"Verified":true}}</string>
        """;

    [Fact]
    public async Task MegaMillions_ParsesRealPayload()
    {
        var handler = new StubHandler(MmFixture);
        var feed = new MegaMillionsJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(new DateOnly(2026, 7, 24), info!.LastDrawDate);
        Assert.Equal(743_000_000m, info.LastJackpot);
        Assert.False(info.LastJackpotWon); // Winners: 0 -> rolled over
        Assert.Equal(800_000_000m, info.NextEstimatedJackpot);
        Assert.Equal(344_200_000m, info.NextCashValue);
    }

    [Fact]
    public async Task Powerball_HtmlResponse_DegradesToNull()
    {
        // powerball.com's retired API route serves the SPA page - must yield null, not throw.
        var handler = new StubHandler("<!DOCTYPE html><html><head><title>Home | Powerball</title></head></html>");
        var feed = new PowerballJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task Powerball_JsonResponse_Parses()
    {
        // Historical shape of the estimates endpoint, kept in case MUSL restores it.
        var handler = new StubHandler(
            """[{"field_next_draw_date":"2026-07-27T22:59:00-04:00","field_prize_amount":"$633 Million","field_prize_amount_cash":"$277.3 Million"}]""");
        var feed = new PowerballJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, info.NextCashValue);
    }

    [Theory]
    [InlineData("$633 Million", 633_000_000L)]
    [InlineData("$277.3 Million", 277_300_000L)]
    [InlineData("$1.5 Billion", 1_500_000_000L)]
    [InlineData("$950,000", 950_000L)]
    [InlineData("", null)]
    [InlineData("TBD", null)]
    public void ParseMoney_HandlesRealFormats(string input, long? expected)
    {
        Assert.Equal((decimal?)expected, PowerballJackpotFeed.ParseMoney(input));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
    }
}
