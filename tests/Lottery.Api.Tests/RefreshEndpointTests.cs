using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Lottery.Api.Tests;

/// <summary>
/// The keep-alive workflow's target: an unguarded refresh runs a full cycle per
/// game and reports it; configuring Refresh:Key turns on the header guard in
/// both directions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RefreshEndpointTests(LotteryApiFactory factory)
{
    // Deliberately descriptive, low-entropy value: this is test wiring, not a credential.
    private const string ConfiguredKey = "integration-test-refresh-key";

    [Fact]
    public async Task NoKeyConfigured_RefreshRunsAndReportsBothGames()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/internal/refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await ApiJson.ReadAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(["Powerball", "MegaMillions"],
            results.Select(r => r.GetProperty("game").GetString()!).ToArray());
        Assert.All(results, r =>
        {
            // Snapshot tails match the pinned clock's schedule and the stub feed
            // has nothing newer, so a cycle is a clean no-op with jackpot data.
            Assert.True(r.GetProperty("upToDate").GetBoolean());
            Assert.Equal(0, r.GetProperty("newDraws").GetInt32());
            Assert.Equal(0, r.GetProperty("skippedInvalid").GetInt32());
            Assert.True(r.GetProperty("jackpotUpdated").GetBoolean());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, r.GetProperty("feedError").ValueKind);
        });
    }

    private HttpClient GuardedClient() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Refresh:Key"] = ConfiguredKey })))
        .CreateClient();

    [Fact]
    public async Task KeyConfigured_MissingHeader_IsUnauthorized()
    {
        using var client = GuardedClient();

        using var response = await client.PostAsync("/internal/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeyConfigured_WrongKey_IsUnauthorized()
    {
        using var client = GuardedClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/refresh");
        request.Headers.Add("X-Refresh-Key", "wrong-value");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeyConfigured_CorrectKey_RunsTheCycle()
    {
        using var client = GuardedClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/refresh");
        request.Headers.Add("X-Refresh-Key", ConfiguredKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await ApiJson.ReadAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(2, results.Length);
    }
}
