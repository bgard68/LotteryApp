using System.Net;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Shared recorded payloads and a recording HTTP handler for feed tests.
/// Fixtures are captured real responses (trimmed), so parsing tests are
/// contracts against the actual wire shapes, not invented ones.
/// </summary>
internal static class FeedFixtures
{
    // megamillions.com GetLatestDrawData, 2026-07-27 (trimmed). Winners: 0 = rollover.
    public const string MegaMillions =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <string xmlns="http://tempuri.org/">{"Drawing":{"PlayDate":"2026-07-24T00:00:00","N1":2,"N2":5,"N3":42,"N4":44,"N5":60,"MBall":1,"Megaplier":-1},"Jackpot":{"PlayDate":"2026-07-24T00:00:00","CurrentPrizePool":743000000.0,"NextPrizePool":800000000.0,"CurrentCashValue":323400000.0,"NextCashValue":344200000.0,"Winners":0,"Verified":true}}</string>
        """;

    // nylottery.ny.gov/nyl-api/games/powerball/draws, 2026-07-27 (trimmed).
    public const string NyLotteryPowerball =
        """
        {"data":{"draws":[
          {"drawTime":1785124800000,"wagerAvailable":true,"estimatedJackpot":633000000,
           "jackpots":[{"amount":633000000,"cashAmount":277300000}],
           "gameId":"15","gameName":"powerball","drawNumber":1978,"status":4},
          {"drawTime":1784952000000,"gameId":"15","gameName":"powerball","drawNumber":1977,"status":22,
           "results":[{"primary":["3","4","24","36","47"],"secondary":["17"]}]}
        ]}}
        """;

    // Historical powerball.com estimates shape, kept in case MUSL restores it.
    public const string PowerballComJson =
        """[{"field_next_draw_date":"2026-07-27T22:59:00-04:00","field_prize_amount":"$633 Million","field_prize_amount_cash":"$277.3 Million"}]""";
}

/// <summary>
/// Stub handler that records every request it sees and answers with a fixed
/// body - lets tests assert both the parsed result and the outgoing contract
/// (URL, headers, call counts).
/// </summary>
internal sealed class RecordingHandler(string body) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        });
    }
}
