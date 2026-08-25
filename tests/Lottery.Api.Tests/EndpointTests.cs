using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lottery.Application.UseCases;

namespace Lottery.Api.Tests;

/// <summary>
/// The endpoints over a real host: routing, model binding, the game-name
/// vocabulary and every error branch each handler can take. The smoke test in
/// CI proves the same paths against a running process; these prove them fast
/// enough to run on every build.
/// </summary>
public sealed class EndpointTests : IClassFixture<LotteryApiFactory>
{
    private readonly HttpClient _client;

    public EndpointTests(LotteryApiFactory factory) => _client = factory.CreateClient();

    // ---------- game-name vocabulary ----------

    [Theory]
    [InlineData("powerball")]
    [InlineData("megamillions")]
    [InlineData("mega-millions")]
    [InlineData("POWERBALL")] // parsing lower-cases first
    public async Task KnownGameNames_AreAccepted(string game)
    {
        var response = await _client.GetAsync($"/api/{game}/rule-eras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/keno/rule-eras")]      // sync WithGame path
    [InlineData("/api/keno/latest")]         // async WithGameAsync path
    [InlineData("/api/keno/next-draw")]
    [InlineData("/api/keno/draws")]
    [InlineData("/api/keno/check?whites=1,2,3,4,5&special=6")]
    [InlineData("/api/keno/generate")]
    public async Task UnknownGame_Is404_OnEveryEndpoint(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("keno", body.GetProperty("error").GetString());
    }

    // ---------- index and health ----------

    [Fact]
    public async Task Root_ListsBothGamesAndTheEndpointCatalogue()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/");

        var games = body.GetProperty("games").EnumerateArray().Select(g => g.GetString()!).ToArray();
        Assert.Equal(["powerball", "megamillions"], games);
        Assert.NotEmpty(body.GetProperty("endpoints").EnumerateArray());
        Assert.Equal("/healthz", body.GetProperty("health").GetString());
    }

    [Fact]
    public async Task Root_HidesTheDocsLink_OutsideDevelopment()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("docs").ValueKind);
    }

    [Fact]
    public async Task Healthz_IsHealthy_OnceSeeded()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    // ---------- reads ----------

    [Fact]
    public async Task NextDraw_ReturnsAScheduledDateForTheGame()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/next-draw");

        Assert.Equal("Powerball", body.GetProperty("game").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("drawDate").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("drawTimeUtc").ValueKind);
    }

    [Fact]
    public async Task Latest_ReportsPendingRatherThanAStaleDraw_WhenTheFeedHasNotCaughtUp()
    {
        // The committed snapshot stops well before today, so the schedule says a
        // drawing has happened that we have no numbers for. Presenting the older
        // draw as "latest" would be a lie; Pending with null numbers is the
        // contract, and it is what the SPA renders its "results pending" state from.
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/latest");

        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whiteBalls").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("special").ValueKind);
    }

    [Theory]
    [InlineData("powerball", "Powerball")]
    [InlineData("megamillions", "Mega Ball")]
    public async Task Latest_NamesTheSpecialBallForTheGame(string game, string specialName)
    {
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/{game}/latest");

        Assert.Equal(specialName, body.GetProperty("specialName").GetString());
    }

    [Fact]
    public async Task Draws_HonoursTheLimitParameter()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/draws?limit=3");

        Assert.Equal(3, body.GetArrayLength());
    }

    [Fact]
    public async Task Draws_FiltersToTheRequestedDateWindow()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>(
            "/api/powerball/draws?from=2024-01-01&to=2024-01-31&limit=100");

        var dates = body.EnumerateArray()
            .Select(d => DateOnly.Parse(d.GetProperty("drawDate").GetString()!))
            .ToArray();

        Assert.NotEmpty(dates);
        Assert.All(dates, d => Assert.InRange(d, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)));
    }

    // ---------- check ----------

    [Fact]
    public async Task Check_WithoutParameters_Is400()
    {
        var response = await _client.GetAsync("/api/powerball/check");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("required", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Check_WithoutSpecial_Is400()
    {
        var response = await _client.GetAsync("/api/powerball/check?whites=1,2,3,4,5");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Check_WithNonNumericWhiteBall_NamesTheOffendingValue()
    {
        var response = await _client.GetAsync("/api/powerball/check?whites=1,2,three,4,5&special=6");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("three", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Check_WithOutOfRangeNumbers_IsRejectedAsAnInvalidTicket()
    {
        var response = await _client.GetAsync("/api/powerball/check?whites=1,2,3,4,99&special=6");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Check_WithAValidTicket_ReportsWhatItCheckedAgainst()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>(
            "/api/powerball/check?whites=1,2,3,4,5&special=6");

        Assert.Equal("Ok", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("drawsChecked").GetInt32() > 0);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("historySince").ValueKind);
    }

    [Fact]
    public async Task Check_TrimsWhitespaceAroundNumbers()
    {
        var response = await _client.GetAsync("/api/powerball/check?whites=1,%202,%203,%204,%205&special=6");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- rule eras ----------

    [Fact]
    public async Task RuleEras_AreOrderedOldestFirst_WithExactlyOneMarkedCurrent()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/rule-eras");

        var eras = body.EnumerateArray().ToArray();
        Assert.NotEmpty(eras);

        var dates = eras.Select(e => DateOnly.Parse(e.GetProperty("effectiveFrom").GetString()!)).ToArray();
        Assert.Equal(dates.OrderBy(d => d), dates);

        Assert.Single(eras, e => e.GetProperty("isCurrent").GetBoolean());
        Assert.All(eras, e =>
        {
            Assert.True(e.GetProperty("whiteBallMax").GetInt32() > 0);
            Assert.True(e.GetProperty("specialBallMax").GetInt32() > 0);
            Assert.Equal(5, e.GetProperty("whiteBallCount").GetInt32());
        });
    }

    [Fact]
    public async Task RuleEras_MarkTheNewestEraCurrent()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/rule-eras");

        var eras = body.EnumerateArray().ToArray();
        Assert.True(eras[^1].GetProperty("isCurrent").GetBoolean());
    }

    // ---------- generate ----------

    [Fact]
    public async Task Generate_DefaultsToASingleTicket()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/generate");

        Assert.Equal(1, body.GetProperty("tickets").GetArrayLength());
    }

    [Fact]
    public async Task Generate_ReturnsFiveSortedDistinctWhitesPerTicket()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/generate?count=5");

        foreach (var ticket in body.GetProperty("tickets").EnumerateArray())
        {
            var whites = ticket.GetProperty("whiteBalls").EnumerateArray().Select(w => w.GetInt32()).ToArray();
            Assert.Equal(5, whites.Length);
            Assert.Equal(whites.Distinct().Count(), whites.Length);
            Assert.Equal(whites.OrderBy(w => w), whites);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GeneratePicks.MaxCount + 1)]
    [InlineData(-1)]
    public async Task Generate_RejectsCountsOutsideTheAllowedRange(int count)
    {
        var response = await _client.GetAsync($"/api/powerball/generate?count={count}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains($"between {GeneratePicks.MinCount} and {GeneratePicks.MaxCount}",
            body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(GeneratePicks.MinCount)]
    [InlineData(GeneratePicks.MaxCount)]
    public async Task Generate_AcceptsBothEndsOfTheAllowedRange(int count)
    {
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/powerball/generate?count={count}");

        Assert.Equal(count, body.GetProperty("tickets").GetArrayLength());
    }

    // ---------- openapi ----------

    [Fact]
    public async Task OpenApiDocument_IsServedOutsideDevelopment()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ScalarUi_IsNotExposedOutsideDevelopment()
    {
        var response = await _client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
