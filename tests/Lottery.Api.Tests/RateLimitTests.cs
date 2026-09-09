using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Lottery.Api.Tests;

/// <summary>
/// Rate limiting on a host with a deliberately tiny permit. The in-memory
/// connection has no remote IP, so every request shares one fixed-window
/// partition. Six requests against a permit of two must trip the limiter even
/// if the burst happens to straddle a window boundary (pigeonhole: one window
/// receives at least three).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(LotteryApiFactory factory)
{
    // UseSetting, not ConfigureAppConfiguration: the permit is read during
    // startup registration, before test config sources are appended.
    private HttpClient TinyPermitClient() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("RateLimit:PermitPerMinute", "2"))
        .CreateClient();

    private static async Task<HttpStatusCode[]> SendBurstAsync(HttpClient client, int count, Func<int, string?> forwardedFor)
    {
        var statuses = new HttpStatusCode[count];
        for (var i = 0; i < count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            var spoof = forwardedFor(i);
            if (spoof is not null)
                request.Headers.Add("X-Forwarded-For", spoof);
            using var response = await client.SendAsync(request);
            statuses[i] = response.StatusCode;
        }

        return statuses;
    }

    [Fact]
    public async Task BurstBeyondThePermit_Gets429_WhileEarlyRequestsSucceed()
    {
        using var client = TinyPermitClient();

        var statuses = await SendBurstAsync(client, 6, _ => null);

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
        using var client = TinyPermitClient();

        var statuses = await SendBurstAsync(client, 6, i => $"10.99.{i}.{i}, 203.0.113.9");

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task DistinctRealClients_GetIndependentBudgets()
    {
        // Six different platform-appended client addresses, one request each:
        // per-client partitioning means nobody is throttled at a permit of two.
        using var client = TinyPermitClient();

        var statuses = await SendBurstAsync(client, 6, i => $"203.0.113.{i + 1}");

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }
}
