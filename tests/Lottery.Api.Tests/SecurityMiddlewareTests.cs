using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lottery.Api.Tests;

/// <summary>
/// The response-hardening middleware. These headers are the whole of the
/// browser-side defence for a JSON-only API, and the CSP in particular is
/// environment-dependent - so it is asserted in both environments rather than
/// assumed from reading the pipeline.
/// </summary>
public sealed class SecurityHeaderTests : IClassFixture<LotteryApiFactory>
{
    private readonly HttpClient _client;

    public SecurityHeaderTests(LotteryApiFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    [InlineData("Cross-Origin-Resource-Policy", "cross-origin")]
    public async Task EverySuccessResponse_CarriesTheHardeningHeaders(string header, string expected)
    {
        var response = await _client.GetAsync("/api/powerball/rule-eras");

        Assert.Equal(expected, Assert.Single(response.Headers.GetValues(header)));
    }

    [Fact]
    public async Task Production_SendsTheLockedDownContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/api/powerball/rule-eras");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", csp);
    }

    // The middleware sits ahead of everything that can short-circuit, so the
    // headers must survive the paths that never reach an endpoint body.
    [Theory]
    [InlineData("/api/keno/latest")]                 // 404 from the game guard
    [InlineData("/api/powerball/check")]             // 400 from parameter validation
    [InlineData("/no/such/route")]                   // 404 from routing itself
    public async Task ErrorResponses_CarryTheHardeningHeadersToo(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    [Fact]
    public async Task ServerHeader_IsNotAdvertised()
    {
        var response = await _client.GetAsync("/api/powerball/rule-eras");

        Assert.DoesNotContain(response.Headers, h =>
            h.Key.Equals("Server", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The Development pipeline differs deliberately: Scalar is a real HTML
/// page, so the 'none' policy is not applied and the docs UI is mapped.</summary>
public sealed class DevelopmentPipelineTests : IClassFixture<DevelopmentApiFactory>
{
    private readonly HttpClient _client;

    public DevelopmentPipelineTests(DevelopmentApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Development_OmitsTheContentSecurityPolicy_SoScalarCanRender()
    {
        var response = await _client.GetAsync("/api/powerball/rule-eras");

        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Development_StillSendsTheOtherHardeningHeaders()
    {
        var response = await _client.GetAsync("/api/powerball/rule-eras");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    [Fact]
    public async Task Development_ExposesTheDocsLinkFromTheIndex()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/");

        Assert.Equal("/scalar", body.GetProperty("docs").GetString());
    }

    [Fact]
    public async Task Development_MapsTheScalarUi()
    {
        var response = await _client.GetAsync("/scalar/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
