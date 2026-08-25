using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Lottery.Api.Tests;

/// <summary>
/// /internal/refresh is the one endpoint that writes, and the only one with an
/// access check. The check is opt-in - configuring no key leaves it open on
/// purpose, for a single-host deployment where nothing else can reach it - so
/// both configurations are pinned here rather than left to inspection.
/// </summary>
public sealed class UnguardedRefreshTests : IClassFixture<LotteryApiFactory>
{
    private readonly HttpClient _client;

    public UnguardedRefreshTests(LotteryApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task WithNoKeyConfigured_TheEndpointIsOpen()
    {
        var response = await _client.PostAsync("/internal/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReportsEveryGame()
    {
        var body = await (await _client.PostAsync("/internal/refresh", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        var games = body.EnumerateArray().Select(r => r.GetProperty("game").GetString()!).ToArray();
        Assert.Equal(["Powerball", "MegaMillions"], games);
    }

    [Fact]
    public async Task Refresh_StoresTheJackpotEstimate_AndNextDrawThenServesIt()
    {
        await _client.PostAsync("/internal/refresh", null);

        var next = await _client.GetFromJsonAsync<JsonElement>("/api/powerball/next-draw");

        Assert.Equal(OfflineJackpotFeed.EstimatedJackpot, next.GetProperty("estimatedJackpot").GetDecimal());
        Assert.Equal(OfflineJackpotFeed.CashValue, next.GetProperty("cashValue").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, next.GetProperty("jackpotUpdatedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Refresh_IsPostOnly()
    {
        var response = await _client.GetAsync("/internal/refresh");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}

/// <summary>The same endpoint with Refresh:Key set - the configuration the
/// deployed instance actually runs, where the keep-alive workflow holds the key.</summary>
public sealed class GuardedRefreshFactory : LotteryApiFactory
{
    public const string Key = "test-refresh-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Settings["Refresh:Key"] = Key;
        base.ConfigureWebHost(builder);
    }
}

public sealed class GuardedRefreshTests : IClassFixture<GuardedRefreshFactory>
{
    private readonly HttpClient _client;

    public GuardedRefreshTests(GuardedRefreshFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task WithoutTheHeader_Is401()
    {
        var response = await _client.PostAsync("/internal/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithTheWrongKey_Is401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/refresh");
        request.Headers.Add("X-Refresh-Key", "not-the-key");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheComparisonIsCaseSensitive()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/refresh");
        request.Headers.Add("X-Refresh-Key", GuardedRefreshFactory.Key.ToUpperInvariant());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithTheCorrectKey_TheRefreshRuns()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/refresh");
        request.Headers.Add("X-Refresh-Key", GuardedRefreshFactory.Key);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARejectedCall_StillCarriesTheHardeningHeaders()
    {
        var response = await _client.PostAsync("/internal/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }
}
