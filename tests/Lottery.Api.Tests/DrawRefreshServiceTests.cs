using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Api.Tests;

/// <summary>
/// The background refresh loop on virtual time: gap-repair at startup, waking
/// five minutes after the next drawing, and polling every ten minutes until the
/// feed publishes. The jackpot feed is called once per game per cycle, which
/// makes its call count the cycle odometer. Real DrawRefreshService and
/// RefreshGame; only ports and the clock are test doubles.
/// </summary>
public sealed class DrawRefreshServiceTests
{
    // Monday 2026-07-27 noon Eastern; PB draw tonight 22:59 ET (02:59 UTC),
    // so the service's next wake is 03:04 UTC (draw + 5 minute feed lag).
    private static readonly DateTimeOffset MondayNoonEt = new(2026, 7, 27, 16, 0, 0, TimeSpan.Zero);

    private static readonly Draw SaturdayPowerball = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 25), [3, 4, 24, 36, 47], 17);
    private static readonly Draw FridayMegaMillions = Draw.Create(Game.MegaMillions, new DateOnly(2026, 7, 24), [2, 5, 42, 44, 60], 1);
    private static readonly Draw MondayPowerball = Draw.Create(Game.Powerball, new DateOnly(2026, 7, 27), [7, 19, 33, 51, 64], 18);

    private sealed record Harness(
        DrawRefreshService Service,
        ServiceProvider Provider,
        InMemoryDrawRepository Repo,
        StubWinningNumbersFeed Numbers,
        StubJackpotFeed Jackpots,
        FakeTimeProvider Time) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private static Harness BuildHarness()
    {
        var repo = new InMemoryDrawRepository();
        repo.Seed(SaturdayPowerball, FridayMegaMillions); // both games current at the pinned time
        var numbers = new StubWinningNumbersFeed();
        var jackpots = new StubJackpotFeed(); // answers null: harmless, but counts cycles
        var time = new FakeTimeProvider(MondayNoonEt);

        var services = new ServiceCollection();
        services.AddSingleton<IDrawRepository>(repo);
        services.AddSingleton<IWinningNumbersFeed>(numbers);
        services.AddSingleton<IJackpotFeed>(jackpots);
        services.AddSingleton<IJackpotStore>(new InMemoryJackpotStore());
        services.AddSingleton<TimeProvider>(time);
        services.AddTransient<RefreshGame>();
        var provider = services.BuildServiceProvider();

        var service = new DrawRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<DrawRefreshService>.Instance);

        return new Harness(service, provider, repo, numbers, jackpots, time);
    }

    /// <summary>
    /// Advances virtual time in fixed steps until the condition holds. The
    /// service registers each fake-time delay a few thread-pool continuations
    /// after its observable side effect, so each step gets a real-time settle
    /// window before the next advance - a single big jump could strand a timer
    /// that had not been registered yet.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, TimeSpan step, Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            time.Advance(step);
            var settle = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            while (!condition() && DateTime.UtcNow < settle)
                await Task.Delay(10);
        }

        Assert.True(condition(), $"Timed out waiting for: {because}");
    }

    [Fact]
    public async Task Startup_RunsOneGapRepairCycle_WithoutFetchingCurrentGames()
    {
        await using var harness = BuildHarness();

        await harness.Service.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 2, "startup refresh for both games");

        Assert.Equal(2, harness.Jackpots.Calls);  // exactly one cycle: two games
        Assert.Equal(0, harness.Numbers.Calls);   // both games current -> numbers feed untouched
    }

    [Fact]
    public async Task WakesAfterTheDrawing_AndStoresThePublishedResult()
    {
        await using var harness = BuildHarness();
        harness.Numbers.Publish(MondayPowerball); // feed will have it by wake time
        await harness.Service.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 2, "startup refresh");

        // Wake is Monday 22:59 ET + 5 min; step past it four hours at a time.
        await AdvanceUntilAsync(harness.Time, TimeSpan.FromHours(4),
            () => harness.Repo.Contains(Game.Powerball, new DateOnly(2026, 7, 27)),
            "Monday's drawing stored after the scheduled wake");
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 4, "wake cycle finishes both games");

        Assert.Equal(1, harness.Numbers.Calls);  // only the behind game was fetched
        Assert.Equal(4, harness.Jackpots.Calls); // startup cycle + wake cycle, nothing more
    }

    [Fact]
    public async Task PollsOnBackoff_UntilTheFeedPublishes_ThenGoesQuiet()
    {
        await using var harness = BuildHarness();
        await harness.Service.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 2, "startup refresh");

        // Wake fires with the feed still empty: first poll finds nothing.
        await AdvanceUntilAsync(harness.Time, TimeSpan.FromHours(4),
            () => harness.Numbers.Calls >= 1, "first poll at wake time");
        Assert.Equal(1, harness.Numbers.Calls);

        // Still nothing published: the next 10-minute poll also comes up empty.
        await AdvanceUntilAsync(harness.Time, TimeSpan.FromMinutes(10),
            () => harness.Numbers.Calls >= 2, "second poll on the backoff interval");
        Assert.Equal(2, harness.Numbers.Calls);
        Assert.False(harness.Repo.Contains(Game.Powerball, new DateOnly(2026, 7, 27)));

        // The feed publishes; the next poll stores it and the loop goes quiet.
        harness.Numbers.Publish(MondayPowerball);
        await AdvanceUntilAsync(harness.Time, TimeSpan.FromMinutes(10),
            () => harness.Repo.Contains(Game.Powerball, new DateOnly(2026, 7, 27)),
            "third poll stores the published drawing");
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 8, "third cycle finishes both games");

        Assert.Equal(3, harness.Numbers.Calls);  // no fourth poll after success
        Assert.Equal(8, harness.Jackpots.Calls); // startup + three polls, two games each
    }

    [Fact]
    public async Task Stop_CancelsTheLoopCleanly()
    {
        await using var harness = BuildHarness();
        await harness.Service.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(() => harness.Jackpots.Calls >= 2, "startup refresh");

        await harness.Service.StopAsync(CancellationToken.None);

        Assert.NotNull(harness.Service.ExecuteTask);
        Assert.True(harness.Service.ExecuteTask!.IsCompletedSuccessfully,
            "cancellation must end the loop, not fault it");
    }
}
