using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

/// <summary>A numbers feed that throws a type RefreshGame's catch filter does not
/// match, for the game named. Stands in for a future escape of the kind that took
/// /internal/refresh to 500 twice.</summary>
public sealed class ExplodingNumbersFeed : IWinningNumbersFeed
{
    public Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct) =>
        game == Game.Powerball
            ? throw new NotSupportedException("feed exploded")
            : Task.FromResult<IReadOnlyList<Draw>>([]);
}

public sealed class ExplodingFeedFactory : LotteryApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWinningNumbersFeed>();
            services.AddSingleton<IWinningNumbersFeed, ExplodingNumbersFeed>();
        });
    }
}

public sealed class RefreshEndpointResilienceTests : IClassFixture<ExplodingFeedFactory>
{
    private readonly HttpClient _client;

    public RefreshEndpointResilienceTests(ExplodingFeedFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task AnUnanticipatedFeedFailure_IsReported_NotA500()
    {
        // Twice now a feed has thrown a type RefreshGame's filter did not match,
        // and this endpoint - which the keep-alive workflow calls - answered 500,
        // reading as the whole instance being down.
        var response = await _client.PostAsync("/internal/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var powerball = body.EnumerateArray().Single(r => r.GetProperty("game").GetString() == "Powerball");
        Assert.Contains("exploded", powerball.GetProperty("feedError").GetString());
    }

    [Fact]
    public async Task OneGameFailing_DoesNotCostTheOtherItsRefresh()
    {
        // The loop used to abort on the first throw, so every game after the
        // failing one was silently skipped.
        var body = await (await _client.PostAsync("/internal/refresh", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        var games = body.EnumerateArray().Select(r => r.GetProperty("game").GetString()!).ToArray();
        Assert.Equal(["Powerball", "MegaMillions"], games);

        var megaMillions = body.EnumerateArray().Single(r => r.GetProperty("game").GetString() == "MegaMillions");
        Assert.Equal(JsonValueKind.Null, megaMillions.GetProperty("feedError").ValueKind);
    }
}
