using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Lottery.Api.Tests;

/// <summary>
/// Rate limiting on a host with a deliberately tiny permit. The permit is read
/// while services are being registered - before the factory's in-memory
/// configuration is appended - so it travels via UseSetting (host
/// configuration), which exists from the first read. The in-memory connection
/// has no remote IP, so every request shares one fixed-window partition; six
/// requests against a permit of two must trip the limiter even if the burst
/// straddles a window boundary (pigeonhole: one window receives at least three).
/// </summary>
public sealed class RateLimitTests : IClassFixture<LotteryApiFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(LotteryApiFactory factory) =>
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("RateLimit:PermitPerMinute", "2"))
            .CreateClient();

    private async Task<HttpStatusCode[]> SendBurstAsync(int count, Func<int, string?> forwardedFor)
    {
        var statuses = new HttpStatusCode[count];
        for (var i = 0; i < count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            var forwarded = forwardedFor(i);
            if (forwarded is not null)
                request.Headers.Add("X-Forwarded-For", forwarded);
            using var response = await _client.SendAsync(request);
            statuses[i] = response.StatusCode;
        }

        return statuses;
    }

    [Fact]
    public async Task BurstBeyondThePermit_Gets429_WhileEarlyRequestsSucceed()
    {
        var statuses = await SendBurstAsync(6, _ => null);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Contains(HttpStatusCode.OK, statuses);
    }

    [Fact]
    public async Task ClientPrependedForwardedEntries_DoNotSplitThePartition()
    {
        // The App Service topology: the front end appends the real client as the
        // RIGHTMOST X-Forwarded-For entry, and ForwardLimit = 1 honors only that
        // one. Attacker-prepended entries to its left vary per request here, yet
        // every request must land in the same partition and trip the limit.
        var statuses = await SendBurstAsync(6, i => $"10.99.{i}.{i}, 203.0.113.9");

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task DistinctRealClients_GetIndependentBudgets()
    {
        // Six different platform-appended client addresses, one request each:
        // per-client partitioning means nobody is throttled at a permit of two.
        var statuses = await SendBurstAsync(6, i => $"203.0.113.{i + 1}");

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }
}
