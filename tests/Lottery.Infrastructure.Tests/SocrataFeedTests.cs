using System.Net;
using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>The adapter behind its fault-tolerance decorator - how it is registered.</summary>
    private static IWinningNumbersFeed Tolerant(StubHandler handler) =>
        new FaultTolerantWinningNumbersFeed(
            Feed(handler), NullLogger<FaultTolerantWinningNumbersFeed>.Instance);

    /// <summary>
    /// Asserts the batch was refused and returns the reported reason. "Refused" means reported,
    /// not thrown and not delivered short: both halves matter, so both are checked here.
    /// </summary>
    private static async Task<string> RefusalReason(IWinningNumbersFeed feed, Game game, DateOnly after)
    {
        var result = await feed.GetDrawsAfterAsync(game, after, CancellationToken.None);

        Assert.False(result.Succeeded, "expected the batch to be refused");
        Assert.Empty(result.Draws);
        Assert.NotNull(result.Error);
        return result.Error!;
    }

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

        Assert.True(draws.Succeeded);
        Assert.Equal(2, draws.Draws.Count);
        var saturday = draws.Draws[1];
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

        var draw = Assert.Single(draws.Draws);
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

        var result = await feed.GetDrawsAfterAsync(Game.Powerball, new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Draws);
    }

    [Fact]
    public async Task JsonNullBody_IsReportedAsAFeedFailure()
    {
        var feed = Feed(new StubHandler("null"));

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 25));

        Assert.Contains("Socrata", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MegaMillionsRowWithoutMegaBall_IsReportedAsAFeedFailure()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44 60"}]"""));

        var reason = await RefusalReason(feed, Game.MegaMillions, new DateOnly(2026, 7, 23));

        Assert.Contains("special ball", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerError_IsReportedAsAFeedFailure()
    {
        // The adapter lets the transport exception out; the decorator it is registered behind
        // turns it into a reported failure. Asserted through the decorator because that pairing
        // is what the Application layer is given - RefreshGame no longer catches by type.
        var feed = Tolerant(new StubHandler("rate limited", HttpStatusCode.TooManyRequests));

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 25));

        Assert.Contains(nameof(HttpRequestException), reason, StringComparison.Ordinal);
    }

    // A bad row still refuses the whole batch - delivering a silently short batch would let
    // gap-repair skip real draws. What it must NOT do is reach the caller as an exception,
    // because that is what turned one malformed row into a 500 from /internal/refresh. There is
    // no longer a type to get right: failure is part of the return value.

    [Fact]
    public async Task MalformedJson_IsReportedAsAFeedError()
    {
        var feed = Tolerant(new StubHandler("""[{"draw_date":"""));

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 25));

        // The cause is named, so the log still says what was actually wrong.
        Assert.Contains("JsonException", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33"}]""")] // fewer than six numbers
    [InlineData("""[{"draw_date":"2026","winning_numbers":"07 19 33 51 64 18"}]""")]           // truncated date
    [InlineData("""[{"winning_numbers":"07 19 33 51 64 18"}]""")]                              // draw_date absent
    public async Task TruncatedRow_RefusesTheBatch_AsAFeedError(string body)
    {
        var feed = Feed(new StubHandler(body));

        await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 25));
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

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 8, 24));

        Assert.Contains("five distinct balls", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonNumericNumbers_RefuseTheBatch_AsAFeedError()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 XX 51 64 18"}]"""));

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 25));

        Assert.Contains("non-numeric ball", reason, StringComparison.Ordinal);
    }
    /// <summary>
    /// The feed publishes partial rows in the minutes after a drawing. A
    /// Powerball row short of its sixth number used to pass the `[..5]` slice
    /// and throw IndexOutOfRangeException on `[5]` - which is not an
    /// ArgumentException, so it escaped this class's own conversion and then
    /// RefreshGame's filter, aborting the refresh before the jackpot step
    /// instead of being reported as a feed error.
    /// </summary>
    [Fact]
    public async Task PowerballRowMissingTheSixthNumber_IsReportedAsAnUnusablePayload()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33 51 64"}]"""));

        var reason = await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 21));

        // Names the shape rather than the symptom: "carries 5, expected 6" is
        // what tells an operator the feed published early, where a generic
        // "unusable payload" would send them looking for a parser bug.
        Assert.Contains("carries 5 number(s), expected 6", reason, StringComparison.Ordinal);
    }

    // The same row shape the other way round: extra numbers are no more usable
    // than missing ones, and silently taking the first six would store a draw
    // nobody drew.
    [Fact]
    public async Task PowerballRowWithTooManyNumbers_IsAlsoRefused()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33 51 64 18 22"}]"""));

        await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 21));
    }

    [Fact]
    public async Task MegaMillionsRowShortOfFiveWhites_IsRefused()
    {
        var feed = Feed(new StubHandler(
            """[{"draw_date":"2026-07-24T00:00:00.000","winning_numbers":"02 05 42 44","mega_ball":"01"}]"""));

        await RefusalReason(feed, Game.MegaMillions, new DateOnly(2026, 7, 23));
    }

    // The batch is refused whole rather than delivered short: one unusable row
    // must not let the good rows through, or a gap-repair silently skips a draw
    // and the ledger never notices it is missing.
    [Fact]
    public async Task OneUnusableRow_RefusesTheWholeBatch()
    {
        var feed = Feed(new StubHandler(
            """
            [{"draw_date":"2026-07-22T00:00:00.000","winning_numbers":"01 02 03 04 05 06"},
             {"draw_date":"2026-07-25T00:00:00.000","winning_numbers":"07 19 33 51 64"}]
            """));

        await RefusalReason(feed, Game.Powerball, new DateOnly(2026, 7, 21));
    }

}
