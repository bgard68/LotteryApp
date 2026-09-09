using System.Net;

namespace Lottery.Api.Tests;

/// <summary>
/// Countdown and latest-result contracts, end to end: schedule math on the
/// pinned clock, snapshot-seeded numbers out of real SQLite, and jackpot
/// figures written by a full refresh cycle through the stub feeds.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NextDrawAndLatestTests(LotteryApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task NextDraw_Powerball_ExactInstantAndStoredJackpot()
    {
        await factory.EnsureJackpotDataAsync(_client);

        using var response = await _client.GetAsync("/api/powerball/next-draw");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Powerball", body.GetProperty("game").GetString());
        // Monday 2026-07-27 22:59 Eastern == Tuesday 02:59 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 2, 59, 0, TimeSpan.Zero),
            body.GetProperty("drawTimeUtc").GetDateTimeOffset());
        Assert.Equal("2026-07-27", body.GetProperty("drawDate").GetString());
        Assert.Equal(633_000_000m, body.GetProperty("estimatedJackpot").GetDecimal());
        Assert.Equal(277_300_000m, body.GetProperty("cashValue").GetDecimal());
        Assert.Equal(LotteryApiFactory.PinnedNowUtc,
            body.GetProperty("jackpotUpdatedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task NextDraw_MegaMillions_ExactInstantAndStoredJackpot()
    {
        await factory.EnsureJackpotDataAsync(_client);

        using var response = await _client.GetAsync("/api/megamillions/next-draw");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        // Tuesday 2026-07-28 23:00 Eastern == Wednesday 03:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 29, 3, 0, 0, TimeSpan.Zero),
            body.GetProperty("drawTimeUtc").GetDateTimeOffset());
        Assert.Equal("2026-07-28", body.GetProperty("drawDate").GetString());
        Assert.Equal(800_000_000m, body.GetProperty("estimatedJackpot").GetDecimal());
        Assert.Equal(344_200_000m, body.GetProperty("cashValue").GetDecimal());
    }

    [Fact]
    public async Task Latest_Powerball_PublishedSnapshotTailWithJackpotStamp()
    {
        await factory.EnsureJackpotDataAsync(_client);

        using var response = await _client.GetAsync("/api/powerball/latest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Powerball", body.GetProperty("game").GetString());
        Assert.Equal("Published", body.GetProperty("status").GetString());
        Assert.Equal("2026-07-25", body.GetProperty("drawDate").GetString());
        Assert.Equal([3, 4, 24, 36, 47], ApiJson.IntArray(body.GetProperty("whiteBalls")));
        Assert.Equal(17, body.GetProperty("special").GetInt32());
        Assert.Equal("Powerball", body.GetProperty("specialName").GetString());
        Assert.Equal(389_000_000m, body.GetProperty("jackpotAmount").GetDecimal());
        Assert.False(body.GetProperty("jackpotWon").GetBoolean());
    }

    [Fact]
    public async Task Latest_MegaMillions_PublishedSnapshotTailWithJackpotStamp()
    {
        await factory.EnsureJackpotDataAsync(_client);

        using var response = await _client.GetAsync("/api/megamillions/latest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Published", body.GetProperty("status").GetString());
        Assert.Equal("2026-07-24", body.GetProperty("drawDate").GetString());
        Assert.Equal([2, 5, 42, 44, 60], ApiJson.IntArray(body.GetProperty("whiteBalls")));
        Assert.Equal(1, body.GetProperty("special").GetInt32());
        Assert.Equal("Mega Ball", body.GetProperty("specialName").GetString());
        Assert.Equal(743_000_000m, body.GetProperty("jackpotAmount").GetDecimal());
        Assert.False(body.GetProperty("jackpotWon").GetBoolean());
    }
}
