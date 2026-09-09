using System.Net;

namespace Lottery.Api.Tests;

/// <summary>
/// Sentinel for the factory itself. Before the service-seam redirect, the
/// factory's database override was silently ignored (the connection string is
/// read at startup registration, before test configuration is appended), so
/// every API test ran against one shared lottery.db in the test bin - and
/// stayed green, because the seed data is identical. This test is what turns
/// that silent misconfiguration into a red build.
/// </summary>
public sealed class LotteryApiFactoryTests : IClassFixture<LotteryApiFactory>
{
    private readonly LotteryApiFactory _factory;

    public LotteryApiFactoryTests(LotteryApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TheHost_WritesTheFactorysOwnDatabase_NotTheDevDefault()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(_factory.DbPath),
            $"expected the API to write {_factory.DbPath}; a missing file means the " +
            "override silently lost to the default connection string");
    }
}
