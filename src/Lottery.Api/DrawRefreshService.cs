using Lottery.Application.UseCases;
using Lottery.Domain;

namespace Lottery.Api;

/// <summary>
/// Wakes shortly after each scheduled drawing and refreshes results/jackpots,
/// polling with backoff until the feed publishes (feeds lag the drawing).
/// Also runs once at startup for gap-repair after downtime. All waiting goes
/// through TimeProvider, so tests can drive this on virtual time - and if the
/// host was asleep at draw time (no Always On), the next startup or
/// /internal/refresh call self-heals.
/// </summary>
public sealed class DrawRefreshService(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<DrawRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan FeedLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollWindow = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAllSafeAsync(stoppingToken); // startup gap-repair

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var nextWake = Enum.GetValues<Game>()
                .Min(g => DrawSchedule.NextDrawUtc(g, now)) + FeedLag;

            try
            {
                await Task.Delay(nextWake - now, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Poll until every game has its latest scheduled drawing stored,
            // or the window closes (next cycle / next startup will catch up).
            var deadline = time.GetUtcNow() + PollWindow;
            while (!stoppingToken.IsCancellationRequested && time.GetUtcNow() < deadline)
            {
                var allCurrent = await RefreshAllSafeAsync(stoppingToken);
                if (allCurrent)
                    break;

                try
                {
                    await Task.Delay(PollInterval, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> RefreshAllSafeAsync(CancellationToken ct)
    {
        var allCurrent = true;
        foreach (var game in Enum.GetValues<Game>())
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var refresh = scope.ServiceProvider.GetRequiredService<RefreshGame>();
                var result = await refresh.ExecuteAsync(game, ct);

                if (result.NewDraws > 0)
                    logger.LogInformation("{Game}: stored {Count} new draw(s).", game, result.NewDraws);
                if (result.SkippedInvalid > 0)
                    logger.LogWarning("{Game}: skipped {Count} era-invalid draw(s) from feed.", game, result.SkippedInvalid);
                if (result.FeedError is not null)
                    logger.LogWarning("{Game}: feed error: {Error}", game, result.FeedError);

                allCurrent &= result.UpToDate;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Game}: refresh cycle failed.", game);
                allCurrent = false;
            }
        }

        return allCurrent;
    }
}
