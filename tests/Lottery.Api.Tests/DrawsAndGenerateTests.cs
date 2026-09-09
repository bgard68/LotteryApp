using System.Net;

namespace Lottery.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class DrawsAndGenerateTests(LotteryApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Draws_Default_ReturnsFiftyNewestFirst()
    {
        using var response = await _client.GetAsync("/api/powerball/draws");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        var rows = body.EnumerateArray().ToArray();
        Assert.Equal(50, rows.Length);
        Assert.Equal("2026-07-25", rows[0].GetProperty("drawDate").GetString());
        Assert.Equal([3, 4, 24, 36, 47], ApiJson.IntArray(rows[0].GetProperty("whiteBalls")));
        Assert.Equal(17, rows[0].GetProperty("special").GetInt32());
        Assert.Equal("2026-07-22", rows[1].GetProperty("drawDate").GetString());
    }

    [Fact]
    public async Task Draws_DateRange_IsInclusiveAtBothEnds()
    {
        using var response = await _client.GetAsync("/api/powerball/draws?from=2026-07-22&to=2026-07-25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal(["2026-07-25", "2026-07-22"],
            body.EnumerateArray().Select(r => r.GetProperty("drawDate").GetString()!).ToArray());
    }

    [Fact]
    public async Task Draws_HugeLimit_IsCappedAtTwoHundred()
    {
        using var response = await _client.GetAsync("/api/powerball/draws?limit=1000000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal(200, body.EnumerateArray().Count());
    }

    [Fact]
    public async Task Draws_ZeroLimit_ClampsUpToOneRow()
    {
        using var response = await _client.GetAsync("/api/powerball/draws?limit=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        var only = Assert.Single(body.EnumerateArray());
        Assert.Equal("2026-07-25", only.GetProperty("drawDate").GetString());
    }

    [Theory]
    [InlineData("/api/powerball/draws?limit=abc")]
    [InlineData("/api/powerball/draws?from=notadate")]
    [InlineData("/api/powerball/draws?to=2026-13-45")]
    public async Task Draws_MalformedQueryValues_AreRejectedByBinding(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Generate_Default_ReturnsOneEraValidTicket()
    {
        using var response = await _client.GetAsync("/api/powerball/generate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Powerball", body.GetProperty("game").GetString());
        var ticket = Assert.Single(body.GetProperty("tickets").EnumerateArray());
        var whites = ApiJson.IntArray(ticket.GetProperty("whiteBalls"));
        Assert.Equal(5, whites.Length);
        Assert.Equal(whites.OrderBy(n => n), whites); // served sorted
        Assert.Equal(5, whites.Distinct().Count());
        Assert.All(whites, w => Assert.InRange(w, 1, 69));
        Assert.InRange(ticket.GetProperty("special").GetInt32(), 1, 26);
    }

    [Fact]
    public async Task Generate_TenTickets_AllValidForTheCurrentEra()
    {
        using var response = await _client.GetAsync("/api/powerball/generate?count=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        var tickets = body.GetProperty("tickets").EnumerateArray().ToArray();
        Assert.Equal(10, tickets.Length);
        Assert.All(tickets, t =>
        {
            var whites = ApiJson.IntArray(t.GetProperty("whiteBalls"));
            Assert.Equal(5, whites.Distinct().Count());
            Assert.All(whites, w => Assert.InRange(w, 1, 69));
            Assert.InRange(t.GetProperty("special").GetInt32(), 1, 26);
        });
    }

    [Fact]
    public async Task Generate_MegaMillions_UsesTheCurrentSeventyBallMatrix()
    {
        using var response = await _client.GetAsync("/api/megamillions/generate?count=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.All(body.GetProperty("tickets").EnumerateArray(), t =>
        {
            Assert.All(ApiJson.IntArray(t.GetProperty("whiteBalls")), w => Assert.InRange(w, 1, 70));
            Assert.InRange(t.GetProperty("special").GetInt32(), 1, 24);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public async Task Generate_OutOfRangeCount_400WithTheContract(int count)
    {
        using var response = await _client.GetAsync($"/api/powerball/generate?count={count}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("count must be between 1 and 10.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Generate_MalformedCount_IsRejectedByBinding()
    {
        using var response = await _client.GetAsync("/api/powerball/generate?count=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
