using System.Threading.RateLimiting;
using Lottery.Api;
using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Lottery.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<DrawRefreshService>();

// Public anonymous API: modest per-client ceiling so a scraper cannot run up
// compute; generous enough that no human ever notices.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Migrations then one-time seed, before serving traffic.
app.Services.GetRequiredService<IDatabaseInitializer>().Initialize();
var importer = app.Services.GetRequiredService<ImportHistory>();
foreach (var game in Enum.GetValues<Game>())
{
    var summary = await importer.ExecuteAsync(game, CancellationToken.None);
    if (!summary.Skipped)
        app.Logger.LogInformation("Seeded {Count} {Game} draws from snapshot.", summary.DrawCount, game);
}

app.UseRateLimiter();
app.MapOpenApi();
app.MapHealthChecks("/healthz");
app.MapLotteryEndpoints();

app.Run();

public partial class Program;
