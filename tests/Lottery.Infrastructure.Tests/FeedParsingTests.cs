using System.Net;
using Lottery.Infrastructure.Feeds;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Contract tests against recorded real payloads, so a silent shape change in
/// the undocumented feeds breaks a test instead of production.
///
/// The second half covers the degradation paths: these endpoints are official
/// sites with no contract, so "the payload lost a field", "the CDN served an
/// HTML challenge page with a 200" and "the host returned a 503" are all
/// ordinary weather. Two of the three adapters are written to return null for
/// all of it; where one is not, the test says so.
/// </summary>
public class FeedParsingTests
{
    // Captured from megamillions.com GetLatestDrawData, 2026-07-27 (trimmed).
    private const string MmFixture =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <string xmlns="http://tempuri.org/">{"Drawing":{"PlayDate":"2026-07-24T00:00:00","N1":2,"N2":5,"N3":42,"N4":44,"N5":60,"MBall":1,"Megaplier":-1},"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"NextPrizePool":800000000.0,"CurrentCashValue":323400000.0,"NextCashValue":344200000.0,"Winners":0,"Verified":true}}</string>
        """;

    /// <summary>The feed's JSON arrives wrapped in the ASMX string envelope.</summary>
    private static string Envelope(string json) =>
        $"""<?xml version="1.0" encoding="utf-8"?><string xmlns="http://tempuri.org/">{json}</string>""";

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
    public async Task MegaMillions_WinnersAboveZero_ReportsJackpotWon()
    {
        var handler = new StubHandler(Envelope(
            """{"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"Winners":2}}"""));
        var feed = new MegaMillionsJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.True(info!.LastJackpotWon);
    }

    [Fact]
    public async Task MegaMillions_MissingFields_DegradeToNulls()
    {
        // Every field is nullable by design; a stripped payload must still yield
        // a JackpotInfo rather than throwing or inventing zeroes.
        var handler = new StubHandler(Envelope("""{"Jackpot":{}}"""));
        var feed = new MegaMillionsJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(Lottery.Domain.Game.MegaMillions, info!.Game);
        Assert.Null(info.LastDrawDate);
        Assert.Null(info.LastJackpot);
        Assert.Null(info.LastJackpotWon);
        Assert.Null(info.NextEstimatedJackpot);
        Assert.Null(info.NextCashValue);
    }

    [Fact]
    public async Task MegaMillions_UnparseablePlayDate_YieldsNullDate()
    {
        var handler = new StubHandler(Envelope(
            """{"Jackpot":{"PlayDate":"not a date","CurrentPrizePool":743000000.0}}"""));
        var feed = new MegaMillionsJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.Null(info!.LastDrawDate);
        Assert.Equal(743_000_000m, info.LastJackpot);
    }

    [Theory]
    [InlineData("")]                            // empty envelope
    [InlineData("   ")]                         // whitespace-only envelope
    [InlineData("null")]                        // JSON null
    [InlineData("""{"Drawing":{"N1":2}}""")]    // drawing only, no jackpot section
    public async Task MegaMillions_PayloadWithoutJackpot_DegradesToNull(string json)
    {
        var feed = new MegaMillionsJackpotFeed(new HttpClient(new StubHandler(Envelope(json))));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None));
    }

    [Fact]
    public async Task MegaMillions_OtherGame_ReturnsNullWithoutCallingTheEndpoint()
    {
        // The composite routes only Mega Millions here; asking for Powerball must
        // not spend an HTTP call on megamillions.com.
        var handler = new StubHandler(MmFixture);
        var feed = new MegaMillionsJackpotFeed(new HttpClient(handler));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    // The three tests below hold this adapter to the promise in its own class
    // comment - "any shape change degrades to null rather than throwing" - and
    // to what its two siblings already do. Each previously escaped as a type
    // RefreshGame's catch filter does not match, which turned a bad upstream
    // response into a 500 from /internal/refresh.

    [Theory]
    [InlineData("""<!DOCTYPE html><html><head><meta charset="utf-8"><title>Access Denied</title></head><body>Blocked</body></html>""")]
    [InlineData("Service Unavailable")]
    public async Task MegaMillions_NonXmlBody_DegradesToNull(string body)
    {
        // The bot-challenge page that already retired powerball.com's API, served
        // with a 200. Not XML, so XDocument.Parse throws - and that must not
        // reach the caller.
        var feed = new MegaMillionsJackpotFeed(new HttpClient(new StubHandler(body)));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Theory]
    [InlineData("""{"Jackpot":{"CurrentPrizePool":""")]  // truncated JSON inside a valid envelope
    [InlineData("Access denied")]                        // envelope-shaped page whose text is not JSON
    public async Task MegaMillions_BodyThatIsNotJson_DegradesToNull(string json)
    {
        var feed = new MegaMillionsJackpotFeed(new HttpClient(new StubHandler(Envelope(json))));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task MegaMillions_ServerError_DegradesToNull()
    {
        var feed = new MegaMillionsJackpotFeed(new HttpClient(
            new StubHandler("upstream unavailable", HttpStatusCode.ServiceUnavailable)));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.Null(info);
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

    [Fact]
    public async Task Powerball_MissingAmountFields_DegradeToNulls()
    {
        var feed = new PowerballJackpotFeed(new HttpClient(new StubHandler(
            """[{"field_next_draw_date":"2026-07-27T22:59:00-04:00"}]""")));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Null(info!.NextEstimatedJackpot);
        Assert.Null(info.NextCashValue);
        // This adapter never carries last-draw data, whatever the payload holds.
        Assert.Null(info.LastDrawDate);
        Assert.Null(info.LastJackpot);
        Assert.Null(info.LastJackpotWon);
    }

    [Theory]
    [InlineData("")]                                        // empty body
    [InlineData("   \n")]                                   // whitespace only
    [InlineData("[]")]                                      // valid JSON, no estimates
    [InlineData("""{"field_prize_amount":"$1 Million"}""")] // object where an array is expected
    [InlineData("[{,}]")]                                   // malformed JSON that still starts with '['
    public async Task Powerball_UnusableBody_DegradesToNull(string body)
    {
        var feed = new PowerballJackpotFeed(new HttpClient(new StubHandler(body)));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task Powerball_ServerError_DegradesToNull()
    {
        var feed = new PowerballJackpotFeed(new HttpClient(
            new StubHandler("bot challenge", HttpStatusCode.Forbidden)));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task Powerball_OtherGame_ReturnsNullWithoutCallingTheEndpoint()
    {
        var handler = new StubHandler("""[{"field_prize_amount":"$633 Million"}]""");
        var feed = new PowerballJackpotFeed(new HttpClient(handler));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    // Captured from nylottery.ny.gov/nyl-api/games/powerball/draws, 2026-07-27 (trimmed).
    private const string NyFixture =
        """
        {"data":{"draws":[
          {"drawTime":1785124800000,"wagerAvailable":true,"estimatedJackpot":633000000,
           "jackpots":[{"amount":633000000,"cashAmount":277300000}],
           "gameId":"15","gameName":"powerball","drawNumber":1978,"status":4},
          {"drawTime":1784952000000,"gameId":"15","gameName":"powerball","drawNumber":1977,"status":22,
           "results":[{"primary":["3","4","24","36","47"],"secondary":["17"]}]}
        ]}}
        """;

    [Fact]
    public async Task NyLottery_ParsesRealPayload()
    {
        var handler = new StubHandler(NyFixture);
        var feed = new NyLotteryJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, info.NextCashValue);
    }

    [Fact]
    public async Task NyLottery_RequestsThePerGameEndpoint()
    {
        var handler = new StubHandler(NyFixture);
        var feed = new NyLotteryJackpotFeed(new HttpClient(handler));

        await feed.GetJackpotAsync(Lottery.Domain.Game.MegaMillions, CancellationToken.None);

        Assert.Equal("https://nylottery.ny.gov/nyl-api/games/megamillions/draws", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task NyLottery_PicksTheLatestDrawCarryingJackpotFigures()
    {
        // Payload order is not guaranteed, and only upcoming draws carry jackpot
        // figures - the newest of those is the one to report.
        var handler = new StubHandler(
            """
            {"data":{"draws":[
              {"drawTime":1785124800000,"estimatedJackpot":500000000,"jackpots":[{"amount":500000000,"cashAmount":230000000}]},
              {"drawTime":1785729600000,"estimatedJackpot":633000000,"jackpots":[{"amount":633000000,"cashAmount":277300000}]},
              {"drawTime":1785902400000,"results":[{"primary":["1","2","3","4","5"],"secondary":["6"]}]}
            ]}}
            """);
        var feed = new NyLotteryJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, info.NextCashValue);
    }

    [Fact]
    public async Task NyLottery_WithoutEstimate_FallsBackToTheJackpotEntry()
    {
        var handler = new StubHandler(
            """{"data":{"draws":[{"drawTime":1785124800000,"jackpots":[{"amount":633000000,"cashAmount":277300000}]}]}}""");
        var feed = new NyLotteryJackpotFeed(new HttpClient(handler));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Equal(277_300_000m, info.NextCashValue);
    }

    [Theory]
    // No jackpots collection at all, and the collection present but not yet
    // priced - both are states NY publishes for a draw that has an estimate
    // before the cash value is settled.
    [InlineData("""{"data":{"draws":[{"drawTime":1785124800000,"estimatedJackpot":633000000}]}}""")]
    [InlineData("""{"data":{"draws":[{"drawTime":1785124800000,"estimatedJackpot":633000000,"jackpots":[]}]}}""")]
    public async Task NyLottery_EstimateWithoutCashValue_ReportsEstimateOnly(string body)
    {
        var feed = new NyLotteryJackpotFeed(new HttpClient(new StubHandler(body)));

        var info = await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None);

        Assert.Equal(633_000_000m, info!.NextEstimatedJackpot);
        Assert.Null(info.NextCashValue);
    }

    [Fact]
    public async Task NyLottery_UnexpectedShape_DegradesToNull()
    {
        var feed = new NyLotteryJackpotFeed(new HttpClient(new StubHandler("""{"unexpected":true}""")));
        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"data":{}}""")]                                     // no draws collection
    [InlineData("""{"data":{"draws":[]}}""")]                           // no draws at all
    [InlineData("""{"data":{"draws":[{"drawTime":1785124800000}]}}""")] // draws, none with figures
    [InlineData("null")]                                                // JSON null
    [InlineData("")]                                                    // empty body
    [InlineData("<!DOCTYPE html><html></html>")]                        // HTML served with a 200
    public async Task NyLottery_UnusableBody_DegradesToNull(string body)
    {
        var feed = new NyLotteryJackpotFeed(new HttpClient(new StubHandler(body)));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Fact]
    public async Task NyLottery_ServerError_DegradesToNull()
    {
        var feed = new NyLotteryJackpotFeed(new HttpClient(
            new StubHandler("gateway timeout", HttpStatusCode.GatewayTimeout)));

        Assert.Null(await feed.GetJackpotAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Theory]
    [InlineData("$633 Million", 633_000_000L)]
    [InlineData("$277.3 Million", 277_300_000L)]
    [InlineData("$1.5 Billion", 1_500_000_000L)]
    [InlineData("$1.5 billion", 1_500_000_000L)] // casing is not guaranteed by the source
    [InlineData("$950,000", 950_000L)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("TBD", null)]
    [InlineData("Pending", null)]
    public void ParseMoney_HandlesRealFormats(string input, long? expected)
    {
        Assert.Equal((decimal?)expected, PowerballJackpotFeed.ParseMoney(input));
    }

    [Fact]
    public void ParseMoney_NullText_IsNull()
    {
        Assert.Null(PowerballJackpotFeed.ParseMoney(null));
    }
}
