using System.Threading.RateLimiting;
using Lottery.Api;
using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Lottery.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHostedService<DrawRefreshService>();

// The health check answers "can this instance actually serve requests?", which
// means asking the database - a process that is running but cannot read its
// data is not healthy, and /healthz is what the keep-alive workflow, the
// deploy gate and Azure all watch.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

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
