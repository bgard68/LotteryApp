using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lottery.Api;

/// <summary>
/// Answers the only question that matters to a load balancer or a deploy gate:
/// can this instance serve real requests? A process that is running but cannot
/// reach its data is not healthy, and saying so turns a silent 500-storm into
/// one actionable signal.
/// </summary>
public sealed class DatabaseHealthCheck(IDrawRepository draws) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Cheap query that proves connectivity, schema, and seeded data in
            // one round trip - the three ways startup has actually failed.
            var count = await draws.CountAsync(Game.Powerball, cancellationToken);

            return count > 0
                ? HealthCheckResult.Healthy($"{count} Powerball draws available.")
                : HealthCheckResult.Degraded(
                    "Database reachable but empty - seeding did not complete.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
