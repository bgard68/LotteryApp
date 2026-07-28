using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Lottery.Api;
using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Lottery.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Free version disclosure otherwise: every response advertised "Kestrel".
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHostedService<DrawRefreshService>();

// The health check answers "can this instance actually serve requests?", which
// means asking the database - a process that is running but cannot read its
// data is not healthy, and /healthz is what the keep-alive workflow, the
// deploy gate and Azure all watch.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Behind App Service the connection's remote address is the platform front
// end, not the visitor - so without this every caller lands in the SAME rate
// limit partition, and one noisy client can 429 the entire site.
//
// ForwardLimit = 1 takes the RIGHTMOST X-Forwarded-For entry, which is the one
// the front end appends. A client that sends its own X-Forwarded-For only adds
// entries to the left of it, so the header cannot be forged to dodge the
// limit. KnownIPNetworks/KnownProxies are cleared because the front end's
// address is neither stable nor knowable. (KnownNetworks is obsolete in
// .NET 10 - KnownIPNetworks is the replacement.)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Public anonymous API: per-client ceiling so a scraper cannot run up compute.
// 120/min default - a multi-ticket check is up to 10 calls, so an active human
// clicking around stays well under it; override via RateLimit:PermitPerMinute.
var permitPerMinute = builder.Configuration.GetValue("RateLimit:PermitPerMinute", 120);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// CORS exists only because the free Static Web Apps SKU has no linked-backend
// feature (that is a Standard-tier capability), so the browser calls this API's
// origin directly instead of a same-origin /api/* proxy. Origins come from
// configuration - set by provisioning, empty locally where the dev proxy makes
// every request same-origin anyway.
const string WebOrigins = "web-origins";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(WebOrigins, policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

// Migrations then one-time seed, before serving traffic.
//
// Startup work that throws makes the host exit, and a platform that restarts
// on exit (App Service, Kubernetes, systemd) will do so indefinitely: each
// attempt repeats the migration and the 4,493-row seed, which is how a single
// missing directory once consumed an entire day's F1 CPU quota in minutes.
// So: fail LOUDLY, and fail ONCE. The app stays up, reports unhealthy, and a
// human sees the cause in one log entry rather than in a restart count.
try
{
    app.Services.GetRequiredService<IDatabaseInitializer>().Initialize();

    var importer = app.Services.GetRequiredService<ImportHistory>();
    foreach (var game in Enum.GetValues<Game>())
    {
        var summary = await importer.ExecuteAsync(game, CancellationToken.None);
        if (!summary.Skipped)
            app.Logger.LogInformation("Seeded {Count} {Game} draws from snapshot.", summary.DrawCount, game);
    }
}
catch (Exception ex)
{
    // No flag needed: the health check asks the database directly, so a failed
    // startup surfaces as an unhealthy /healthz without any state to track.
    app.Logger.LogCritical(ex,
        "DATABASE STARTUP FAILED. The API will report unhealthy and serve 503s "
        + "rather than restart-looping. Connection string in use: {ConnectionString}",
        // The SQLite path is not a secret; a SQL Server string uses Managed
        // Identity and carries no password. Logging it turns "it won't start"
        // into "it cannot open THIS path", which is the whole diagnosis.
        builder.Configuration.GetConnectionString("Default"));
}

// First in the pipeline, and before the rate limiter in particular: it is what
// turns Connection.RemoteIpAddress into the real caller.
app.UseForwardedHeaders();

// Security headers on every response, including error responses - which is why
// this sits before everything that can short-circuit (CORS preflight, the rate
// limiter's 429, the endpoints' 400s).
//
// This API returns JSON and nothing else: it never serves a document, never
// loads a subresource, and has no reason to be embedded. So the policy is the
// most restrictive one that exists rather than a curated allowlist.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Cross-Origin-Resource-Policy"] = "cross-origin"; // the SWA origin must still fetch it

    // Scalar (Development only) is a real HTML page and would break under a
    // 'none' policy, so the lockdown applies where there is no such page.
    if (!app.Environment.IsDevelopment())
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

    await next();
});

if (!app.Environment.IsDevelopment())
{
    // App Service already 301s HTTP to HTTPS; this tells the browser to stop
    // trying plaintext at all, which closes the first-request window.
    app.UseHsts();
}

app.UseCors(WebOrigins);
app.UseRateLimiter();
app.MapOpenApi();

// Interactive API reference at /scalar, development only - the OpenAPI
// document itself stays available everywhere, but a browsable UI is not
// something a public production API needs to expose.
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options => options.WithTitle("LotteryApp API"));
}
app.MapHealthChecks("/healthz");
app.MapLotteryEndpoints();

app.Run();

public partial class Program;
