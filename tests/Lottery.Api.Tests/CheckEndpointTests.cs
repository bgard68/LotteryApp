using System.Net;
using System.Text.Json;

namespace Lottery.Api.Tests;

/// <summary>
/// The ticket checker over the full stack: query parsing, use-case validation
/// against the current era, and the set-wise SQL match over all ~1,971 seeded
/// Powerball drawings. The winning-ticket case uses the snapshot's own final
/// draw, so the expected jackpot hit is committed data, not luck.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CheckEndpointTests(LotteryApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task KnownWinningTicket_ReportsTheJackpotMatchAgainstFullHistory()
    {
        using var response = await _client.GetAsync("/api/powerball/check?whites=3,4,24,36,47&special=17");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Ok", body.GetProperty("status").GetString());
        Assert.Equal("2010-02-03", body.GetProperty("historySince").GetString()); // dataset start
        Assert.InRange(body.GetProperty("drawsChecked").GetInt32(), 1900, 1_000_000);

        var jackpotHit = body.GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("drawDate").GetString() == "2026-07-25");
        Assert.Equal(5, jackpotHit.GetProperty("whiteMatches").GetInt32());
        Assert.True(jackpotHit.GetProperty("specialMatched").GetBoolean());
        Assert.Equal("Match 5 + Powerball", jackpotHit.GetProperty("tier").GetString());
        Assert.True(jackpotHit.GetProperty("isJackpot").GetBoolean());
        Assert.Equal(JsonValueKind.Null, jackpotHit.GetProperty("approximateAmount").ValueKind);
        Assert.Equal([3, 4, 24, 36, 47], ApiJson.IntArray(jackpotHit.GetProperty("drawnWhiteBalls")));
        Assert.Equal(17, jackpotHit.GetProperty("drawnSpecial").GetInt32());
    }

    [Fact]
    public async Task WhitesInAnyOrder_ProduceTheSameJackpotMatch()
    {
        using var response = await _client.GetAsync("/api/powerball/check?whites=47,3,36,4,24&special=17");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        var jackpotHit = body.GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("drawDate").GetString() == "2026-07-25");
        Assert.Equal(5, jackpotHit.GetProperty("whiteMatches").GetInt32());
    }

    [Fact]
    public async Task MissingParameters_400WithInstructions()
    {
        using var response = await _client.GetAsync("/api/powerball/check");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Query parameters 'whites' (5 comma-separated numbers) and 'special' are required.",
            body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NonNumericWhite_400NamingTheOffender()
    {
        using var response = await _client.GetAsync("/api/powerball/check?whites=1,2,3,x,5&special=6");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("'x' is not a number.", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("1,2,3,4", 5, "Exactly 5 white balls are required.")]
    [InlineData("1,2,3,4,5,6", 7, "Exactly 5 white balls are required.")]
    [InlineData("1,2,3,4,4", 5, "White balls must be distinct.")]
    [InlineData("1,2,3,4,70", 5, "White balls must be between 1 and 69.")]
    [InlineData("1,2,3,4,5", 27, "Powerball must be between 1 and 26.")]
    [InlineData("1,2,3,4,5", 0, "Powerball must be between 1 and 26.")]
    public async Task InvalidPowerballTickets_400WithTheExactReason(string whites, int special, string expectedError)
    {
        using var response = await _client.GetAsync($"/api/powerball/check?whites={whites}&special={special}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MegaMillions_SpecialBound_UsesMegaBallNameAndCurrentEra()
    {
        using var response = await _client.GetAsync("/api/megamillions/check?whites=1,2,3,4,5&special=25");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Mega Ball must be between 1 and 24.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NonNumericSpecial_IsRejectedByBinding()
    {
        using var response = await _client.GetAsync("/api/powerball/check?whites=1,2,3,4,5&special=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
