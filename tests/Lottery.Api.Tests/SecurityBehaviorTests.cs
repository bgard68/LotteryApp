using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lottery.Api.Tests;

/// <summary>
/// The header middleware, environment gating (CSP/HSTS/Scalar are
/// production-vs-development decisions), and config-driven CORS. The in-memory
/// server never adds a Server header, so that particular absence is asserted by
/// the PowerShell smoke test against real Kestrel, not here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityBehaviorTests(LotteryApiFactory factory)
{
    // Host allowed by appsettings' AllowedHosts and not on HSTS's localhost
    // exclusion list - required for the production-mode assertions.
    private static readonly Uri ProductionBaseAddress = new("https://app-lottery-8e49d22b.azurewebsites.net");

    private static void AssertBaselineSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("cross-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Resource-Policy")));
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnSuccessResponses()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBaselineSecurityHeaders(response);
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnErrorResponsesToo()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/euromillions/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertBaselineSecurityHeaders(response);
    }

    [Fact]
    public async Task Development_DoesNotSendCsp_AndServesScalar()
    {
        using var client = factory.CreateClient();

        using var root = await client.GetAsync("/");
        using var scalar = await client.GetAsync("/scalar");

        Assert.False(root.Headers.Contains("Content-Security-Policy"),
            "CSP would break the Scalar page, so Development must not send it");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
    }

    [Fact]
    public async Task Production_LocksDownCspHstsAndHidesDocs()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"))
            .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = ProductionBaseAddress });

        using var root = await client.GetAsync("/");
        using var scalar = await client.GetAsync("/scalar");
        using var openApi = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        AssertBaselineSecurityHeaders(root);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            Assert.Single(root.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("max-age", Assert.Single(root.Headers.GetValues("Strict-Transport-Security")));

        var body = await ApiJson.ReadAsync(root);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("docs").ValueKind); // no Scalar link
        Assert.Equal(HttpStatusCode.NotFound, scalar.StatusCode);             // and no Scalar page
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);                  // document stays public
    }

    [Fact]
    public async Task Cors_ConfiguredOrigin_IsEchoedBack()
    {
        // UseSetting, not ConfigureAppConfiguration: origins are read during
        // startup registration, before test config sources are appended.
        using var client = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Cors:AllowedOrigins:0", "https://lottery.example"))
            .CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/powerball/rule-eras");
        request.Headers.Add("Origin", "https://lottery.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://lottery.example",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Cors_UnconfiguredOrigin_GetsNoAllowHeader()
    {
        using var client = factory.CreateClient(); // no origins configured
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/powerball/rule-eras");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
