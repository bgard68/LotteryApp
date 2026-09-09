using System.Net;
using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Microsoft.Extensions.Configuration;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The live NY Open Data client, driven entirely from canned payloads. Two
/// things matter here: the query it builds (a wrong $where silently re-imports
/// or silently misses draws) and the row -> Draw projection, which differs
/// between the two datasets - Powerball packs six numbers into one string,
/// Mega Millions keeps the Mega Ball in its own column.
/// </summary>
public class SocrataFeedTests
{
    private static IConfiguration Config(string? appToken = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feeds:SocrataAppToken"] = appToken })
            .Build();

    private static SocrataWinningNumbersFeed Feed(StubHandler handler, string? appToken = null) =>
        new(new HttpClient(handler), Config(appToken));

    // Two real rows from resource/d6yy-54nr.json (Powerball).
    private const string PowerballRows =
        """
        [{"draw_date":"2026-07-22T00:00:00.000","winning_numbers":"01 02 03 04 05 06","multiplier":"2"},
         {"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33 51 64 18","multiplier":"3"}]
        """;

    // Real shape of resource/5xaw-6ayf.json (Mega Millions): five whites, mega_ball apart.
    private const string MegaMillionsRows =
        """
        [{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44 60","mega_ball":"01","multiplier":"3"}]
        """;

    [Fact]
    public async Task Powerball_TakesTheSixthNumberAsThePowerball()
    {
        var feed = Feed(new StubHandler(PowerballRows));

        var draws = await feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 21), CancellationToken.None);

        Assert.Equal(2, draws.Count);
        var saturday = draws[1];
        Assert.Equal(Game.Powerball, saturday.Game);
        Assert.Equal(new DateOnly(2026, 7, 25), saturday.DrawDate);
        Assert.Equal([7, 19, 33, 51, 64], saturday.WhiteBalls);
        Assert.Equal(18, saturday.Special);
        // Socrata carries no jackpot data; those are filled in later by the jackpot feed.
        Assert.Null(saturday.JackpotAmount);
        Assert.Null(saturday.JackpotWon);
    }

    [Fact]
    public async Task MegaMillions_TakesTheSpecialFromTheMegaBallColumn()
    {
        var feed = Feed(new StubHandler(MegaMillionsRows));

        var draws = await feed.GetDrawsAfterAsync(Game.MegaMillions, new DateOnly(2026, 7, 23), CancellationToken.None);

        var draw = Assert.Single(draws);
        Assert.Equal(Game.MegaMillions, draw.Game);
        Assert.Equal(new DateOnly(2026, 7, 24), draw.DrawDate);
        Assert.Equal([2, 5, 42, 44, 60], draw.WhiteBalls);
        Assert.Equal(1, draw.Special);
    }

    [Theory]
    [InlineData(Game.Powerball, "/resource/d6yy-54nr.json")]
    [InlineData(Game.MegaMillions, "/resource/5xaw-6ayf.json")]
    public async Task EachGameQueriesItsOwnDataset(Game game, string expectedPath)
    {
        var handler = new StubHandler("[]");

        await Feed(handler).GetDrawsAfterAsync(game, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.Equal("data.ny.gov", handler.LastUri!.Host);
        Assert.Equal(expectedPath, handler.LastUri.AbsolutePath);
    }

    [Fact]
    public async Task AsksOnlyForDrawsAfterTheEndOfTheGivenDay()
    {
        // The boundary is what makes refresh a gap-repair rather than a re-import:
        // "> that date at 23:59:59" means the caller's own latest draw is excluded.
        var handler = new StubHandler("[]");

        await Feed(handler).GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("draw_date > '2026-07-25T23:59:59'", query, StringComparison.Ordinal);
        Assert.Contains("$order=draw_date", query, StringComparison.Ordinal);
        Assert.Contains("$limit=200", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredAppToken_IsSentAsAHeader()
    {
        var handler = new StubHandler("[]");

        await Feed(handler, "s3cr3t-token").GetDrawsAfterAsync(
            Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.Equal(["s3cr3t-token"], handler.LastAppTokenHeaders);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutAnAppToken_TheHeaderIsOmitted(string? appToken)
    {
        // The token only raises rate limits - the feed must still work unauthenticated,
        // and must never send an empty header value.
        var handler = new StubHandler("[]");

        await Feed(handler, appToken).GetDrawsAfterAsync(
            Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.Empty(handler.LastAppTokenHeaders);
    }

    [Fact]
    public async Task EmptyDataset_YieldsNoDraws()
    {
        var feed = Feed(new StubHandler("[]"));

        Assert.Empty(await feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));
    }

    [Fact]
    public async Task JsonNullBody_IsReportedAsAFeedFailure()
    {
        var feed = Feed(new StubHandler("null"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));
        Assert.Contains("Socrata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MegaMillionsRowWithoutMegaBall_IsReportedAsAFeedFailure()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44 60"}]"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.MegaMillions, new DateOnly(2026, 7, 23), CancellationToken.None));
        Assert.Contains("mega_ball", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerError_SurfacesAsHttpRequestException()
    {
        // RefreshGame catches this one and reports it as a feed error.
        var feed = Feed(new StubHandler("rate limited", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));
    }

    // A bad row still refuses the whole batch - delivering a silently short
    // batch would let gap-repair skip real draws. What it must NOT do is
    // surface a type RefreshGame's catch filter misses, because that turns one
    // malformed row into a 500 from /internal/refresh instead of a reported
    // feed error. InvalidOperationException is the type that filter handles.

    [Fact]
    public async Task MalformedJson_IsReportedAsAFeedError()
    {
        var feed = Feed(new StubHandler("""[{"draw_date":"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));

        // The cause is kept, so the log still says what was actually wrong.
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Theory]
    [InlineData("""[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33"}]""")] // fewer than six numbers
    [InlineData("""[{"draw_date":"2026","winning_numbers":"07 19 33 51 64 18"}]""")]           // truncated date
    [InlineData("""[{"winning_numbers":"07 19 33 51 64 18"}]""")]                              // draw_date absent
    public async Task TruncatedRow_RefusesTheBatch_AsAFeedError(string body)
    {
        var feed = Feed(new StubHandler(body));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));
    }

    [Theory]
    // The row shape the feed publishes in the minutes AFTER a drawing, before
    // the real numbers land. This is what took /internal/refresh to 500 in
    // production: Draw.Create throws plain ArgumentException here, and an
    // earlier version of this guard caught only ArgumentOutOfRangeException -
    // its subclass - so the row sailed straight through.
    [InlineData("""[{"draw_date":"2026-08-25T00:00:00.000","winning_numbers":"00 00 00 00 00 00"}]""")]
    [InlineData("""[{"draw_date":"2026-08-25T00:00:00.000","winning_numbers":"07 07 33 51 64 18"}]""")]
    public async Task APlaceholderRow_IsReportedAsAFeedError_NotThrownAtTheCaller(string body)
    {
        var feed = Feed(new StubHandler(body));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 8, 24), CancellationToken.None));

        Assert.IsAssignableFrom<ArgumentException>(ex.InnerException);
    }

    [Fact]
    public async Task NonNumericNumbers_RefuseTheBatch_AsAFeedError()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 XX 51 64 18"}]"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None));

        Assert.IsType<FormatException>(ex.InnerException);
    }
}
