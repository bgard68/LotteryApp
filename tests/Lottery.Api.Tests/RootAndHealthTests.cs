using System.Net;

namespace Lottery.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RootAndHealthTests(LotteryApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Root_DescribesGamesEndpointsAndDocs()
    {
        using var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("LotteryApp API", body.GetProperty("name").GetString());
        Assert.Equal(["powerball", "megamillions"], ApiJson.StringArray(body.GetProperty("games")));
        Assert.Equal("/openapi/v1.json", body.GetProperty("openApi").GetString());
        Assert.Equal("/healthz", body.GetProperty("health").GetString());
        Assert.Equal("/scalar", body.GetProperty("docs").GetString()); // test host runs as Development
        Assert.Equal(
            [
                "/api/{game}/next-draw",
                "/api/{game}/latest",
                "/api/{game}/draws?from=&to=&limit=",
                "/api/{game}/check?whites=1,2,3,4,5&special=6",
                "/api/{game}/rule-eras",
                "/api/{game}/generate?count=1",
            ],
            ApiJson.StringArray(body.GetProperty("endpoints")));
    }

    [Fact]
    public async Task Healthz_WithSeededDatabase_ReportsHealthy()
    {
        using var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Host_UsesTheTestDatabaseFile_NotTheDevDefault()
    {
        // Guards the config override itself: if the in-memory connection string
        // stopped winning, tests would silently run against a repo-root lottery.db.
        using var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(factory.DbPath), $"expected the API to write {factory.DbPath}");
    }

    [Fact]
    public async Task OpenApiDocument_IsServed()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.True(body.TryGetProperty("openapi", out var version));
        Assert.False(string.IsNullOrEmpty(version.GetString()));
        Assert.True(body.GetProperty("paths").EnumerateObject().Any(), "document should describe at least one path");
    }

    [Theory]
    [InlineData("/api/euromillions/next-draw")]
    [InlineData("/api/euromillions/latest")]
    [InlineData("/api/euromillions/draws")]
    [InlineData("/api/euromillions/check?whites=1,2,3,4,5&special=6")]
    [InlineData("/api/euromillions/rule-eras")]
    [InlineData("/api/euromillions/generate")]
    public async Task UnknownGame_Returns404WithGuidance(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal("Unknown game 'euromillions'. Use 'powerball' or 'megamillions'.",
            body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/api/POWERBALL/latest", "Powerball")]
    [InlineData("/api/Powerball/latest", "Powerball")]
    [InlineData("/api/megamillions/latest", "MegaMillions")]
    [InlineData("/api/mega-millions/latest", "MegaMillions")] // documented alias
    public async Task GameRouting_AcceptsAnyCasingAndTheAlias(string path, string expectedGame)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiJson.ReadAsync(response);
        Assert.Equal(expectedGame, body.GetProperty("game").GetString());
    }

    [Fact]
    public async Task CheckEndpoint_RejectsPost()
    {
        using var response = await _client.PostAsync("/api/powerball/check?whites=1,2,3,4,5&special=6", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
