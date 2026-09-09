using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Microsoft.Extensions.Configuration;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The Socrata client is the live gap-repair source; these are contract tests
/// for both directions: what it asks for (dataset, incremental $where, limit,
/// optional token header) and how it maps the two games' different row shapes.
/// </summary>
public class SocrataFeedTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithToken(string token) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Feeds:SocrataAppToken"] = token })
        .Build();

    private static SocrataWinningNumbersFeed FeedOver(RecordingHandler handler, IConfiguration? config = null) =>
        new(new HttpClient(handler), config ?? EmptyConfig());

    [Fact]
    public async Task Powerball_SixNumberRow_SplitsWhitesAndSpecial_AndSortsWhites()
    {
        // winning_numbers arrives space-separated with the Powerball last;
        // deliberately unsorted here to prove normalization.
        var handler = new RecordingHandler(
            """[{"draw_date":"2026-07-27T00:00:00.000","winning_numbers":"33 07 64 19 51 18"}]""");

        var draws = await FeedOver(handler).GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        var draw = Assert.Single(draws);
        Assert.Equal(Game.Powerball, draw.Game);
        Assert.Equal(new DateOnly(2026, 7, 27), draw.DrawDate);
        Assert.Equal([7, 19, 33, 51, 64], draw.WhiteBalls);
        Assert.Equal(18, draw.Special);
    }

    [Fact]
    public async Task MegaMillions_MegaBallField_IsTheSpecial()
    {
        var handler = new RecordingHandler(
            """[{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44 60","mega_ball":"1"}]""");

        var draws = await FeedOver(handler).GetDrawsAfterAsync(Game.MegaMillions, new DateOnly(2026, 7, 21), CancellationToken.None);

        var draw = Assert.Single(draws);
        Assert.Equal([2, 5, 42, 44, 60], draw.WhiteBalls);
        Assert.Equal(1, draw.Special);
    }

    [Fact]
    public async Task MegaMillions_RowMissingMegaBall_Throws()
    {
        var handler = new RecordingHandler(
            """[{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44 60"}]""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FeedOver(handler).GetDrawsAfterAsync(Game.MegaMillions, new DateOnly(2026, 7, 21), CancellationToken.None));

        Assert.Contains("mega_ball", ex.Message);
    }

    [Fact]
    public async Task EmptyFeed_ReturnsEmptyList()
    {
        var draws = await FeedOver(new RecordingHandler("[]"))
            .GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.Empty(draws);
    }

    [Fact]
    public async Task Request_TargetsThePowerballDataset_WithIncrementalWhere()
    {
        var handler = new RecordingHandler("[]");

        await FeedOver(handler).GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var url = Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri);
        Assert.StartsWith("https://data.ny.gov/resource/d6yy-54nr.json", url);
        Assert.Contains("draw_date > '2026-07-25T23:59:59'", url); // strictly-after gap repair
        Assert.Contains("$order=draw_date", url);
        Assert.Contains("$limit=200", url);
    }

    [Fact]
    public async Task Request_TargetsTheMegaMillionsDataset()
    {
        var handler = new RecordingHandler("[]");

        await FeedOver(handler).GetDrawsAfterAsync(Game.MegaMillions, new DateOnly(2026, 7, 24), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("5xaw-6ayf.json", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task AppToken_WhenConfigured_IsSentAsHeader()
    {
        var handler = new RecordingHandler("[]");

        await FeedOver(handler, ConfigWithToken("not-a-real-token"))
            .GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.TryGetValues("X-App-Token", out var values));
        Assert.Equal("not-a-real-token", Assert.Single(values!));
    }

    [Fact]
    public async Task AppToken_WhenAbsent_NoHeaderIsSent()
    {
        var handler = new RecordingHandler("[]");

        await FeedOver(handler).GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("X-App-Token"));
    }
}
