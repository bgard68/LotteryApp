using System.Net;

namespace Lottery.Api.Tests;

/// <summary>
/// The frontend validates tickets against this payload, so the era table's
/// shape over HTTP - ordering, bounds, exactly one current era - is a contract.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RuleErasEndpointTests(LotteryApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Powerball_SevenErasAscending_CurrentIsTheSixtyNineMatrix()
    {
        using var response = await _client.GetAsync("/api/powerball/rule-eras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var eras = (await ApiJson.ReadAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(7, eras.Length);

        Assert.Equal("1992-04-22", eras[0].GetProperty("effectiveFrom").GetString());
        Assert.Equal(45, eras[0].GetProperty("whiteBallMax").GetInt32());
        Assert.Equal(45, eras[0].GetProperty("specialBallMax").GetInt32());
        Assert.False(eras[0].GetProperty("isCurrent").GetBoolean());

        Assert.Equal("2015-10-07", eras[^1].GetProperty("effectiveFrom").GetString());
        Assert.Equal(69, eras[^1].GetProperty("whiteBallMax").GetInt32());
        Assert.Equal(26, eras[^1].GetProperty("specialBallMax").GetInt32());
        Assert.True(eras[^1].GetProperty("isCurrent").GetBoolean());

        Assert.Single(eras, e => e.GetProperty("isCurrent").GetBoolean());
        Assert.All(eras, e => Assert.Equal(5, e.GetProperty("whiteBallCount").GetInt32()));
    }

    [Fact]
    public async Task MegaMillions_FiveEras_CurrentIsTheApril2025Revamp()
    {
        using var response = await _client.GetAsync("/api/megamillions/rule-eras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var eras = (await ApiJson.ReadAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(5, eras.Length);
        Assert.Equal("2025-04-08", eras[^1].GetProperty("effectiveFrom").GetString());
        Assert.Equal(70, eras[^1].GetProperty("whiteBallMax").GetInt32());
        Assert.Equal(24, eras[^1].GetProperty("specialBallMax").GetInt32());
        Assert.Single(eras, e => e.GetProperty("isCurrent").GetBoolean());
    }
}
