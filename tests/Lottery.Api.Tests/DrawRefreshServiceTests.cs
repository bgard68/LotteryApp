using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Api.Tests;

/// <summary>
/// The refresh loop on virtual time. Every wait in the service goes through
/// TimeProvider precisely so this is possible - a real-clock test of a service
/// that sleeps until the next drawing would take days.
/// </summary>
public sealed class DrawRefreshServiceTests
{
    // A Wednesday, comfortably between drawings.
    private static readonly DateTimeOffset Start = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    private static (DrawRefreshService Service, RecordingRefresh Refresh, FakeTimeProvider Time) Build()
    {
        var refresh = new RecordingRefresh();
        var services = new ServiceCollection()
            .AddSingleton<IDrawRepository>(refresh)
            .AddSingleton<IWinningNumbersFeed>(refresh)
            .AddSingleton<IJackpotFeed>(refresh)
            .AddSingleton<IJackpotStore>(refresh)
            .AddSingleton(TimeProvider.System)
            .AddTransient<RefreshGame>()
            .BuildServiceProvider();

        var time = new FakeTimeProvider(Start);
        var service = new DrawRefreshService(
            services.GetRequiredService<IServiceScopeFactory>(),
            time,
            NullLogger<DrawRefreshService>.Instance);

        return (service, refresh, time);
    }

    [Fact]
    public async Task StartUp_RefreshesEveryGameImmediately_ToRepairGapsAfterDowntime()
    {
        var (service, refresh, _) = Build();

        await service.StartAsync(CancellationToken.None);
        await refresh.WaitForIdleAsync();
        await service.StopAsync(CancellationToken.None);

        // The startup pass is what self-heals a host that was asleep at draw
        // time, so it must cover both games before any waiting happens.
        Assert.Contains(Game.Powerball, refresh.Games);
        Assert.Contains(Game.MegaMillions, refresh.Games);
    }

    [Fact]
    public async Task AFailingGame_DoesNotStopTheOtherOne()
    {
        var (service, refresh, _) = Build();
        refresh.ThrowFor = Game.Powerball;

        await service.StartAsync(CancellationToken.None);
        await refresh.WaitForIdleAsync();
        await service.StopAsync(CancellationToken.None);

        // One game's feed being down is routine; it must not cost the other
        // game its refresh, and it must not take the host down.
        Assert.Contains(Game.MegaMillions, refresh.Games);
    }

    [Fact]
    public async Task StoppingDuringTheWait_ShutsDownCleanly()
    {
        var (service, refresh, _) = Build();

        await service.StartAsync(CancellationToken.None);
        await refresh.WaitForIdleAsync();

        // The service is now parked in Task.Delay until the next drawing. A
        // host shutting down at that moment is the normal case, and it must
        // not surface the cancellation as a fault.
        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AfterTheNextDrawing_ItWakesAndRefreshesAgain()
    {
        var (service, refresh, time) = Build();

        await service.StartAsync(CancellationToken.None);
        var afterStartup = await refresh.WaitForIdleAsync();

        // Advance in steps rather than one jump: the service registers its
        // timer only once the startup pass has fully unwound, so a single
        // Advance can land before that timer exists and never fire it.
        var woke = await AdvanceUntilAsync(time, () => refresh.Calls > afterStartup);

        await service.StopAsync(CancellationToken.None);

        Assert.True(woke, "the service never refreshed again after the drawing time passed");
    }

    /// <summary>
    /// Pushes virtual time forward in 30-minute steps until the condition holds,
    /// yielding between steps so the service's continuations can run.
    /// </summary>
    private static async Task<bool> AdvanceUntilAsync(FakeTimeProvider time, Func<bool> condition)
    {
        for (var i = 0; i < 400; i++)
        {
            if (condition()) return true;
            time.Advance(TimeSpan.FromMinutes(30));
            await Task.Delay(5);
        }

        return condition();
    }

    /// <summary>
    /// Stands in for the whole refresh dependency graph and records which games
    /// were asked for.
    /// </summary>
    private sealed class RecordingRefresh : IDrawRepository, IWinningNumbersFeed, IJackpotFeed, IJackpotStore
    {
        private readonly Lock _gate = new();
        private readonly List<Game> _games = [];

        public Game? ThrowFor { get; set; }

        public Game[] Games
        {
            get { lock (_gate) return [.. _games]; }
        }

        public int Calls
        {
            get { lock (_gate) return _games.Count; }
        }

        /// <summary>
        /// Waits until the service stops touching the repository, and returns the
        /// call count at that point. The number of repository reads per refresh
        /// cycle is RefreshGame's business, so the test watches for the loop to
        /// go quiet rather than counting to a figure it would have to keep in
        /// step with that use case.
        /// </summary>
        public async Task<int> WaitForIdleAsync()
        {
            var previous = -1;
            for (var i = 0; i < 200; i++)
            {
                var current = Calls;
                if (current > 0 && current == previous)
                    return current;

                previous = current;
                await Task.Delay(10);
            }

            throw new TimeoutException("The refresh loop never went idle.");
        }

        public Task<Draw?> GetLatestAsync(Game game, CancellationToken ct)
        {
            lock (_gate) _games.Add(game);
            if (ThrowFor == game) throw new InvalidOperationException("feed down");

            // A stored draw dated in the far future means "nothing outstanding",
            // which ends the poll loop after a single pass.
            return Task.FromResult<Draw?>(Draw.Create(
                game, new DateOnly(2099, 1, 1), [1, 2, 3, 4, 5], 6, null, null));
        }

        public Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Draw>>([]);

        public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) =>
            Task.FromResult<JackpotInfo?>(null);

        public Task<JackpotEstimate?> GetAsync(Game game, CancellationToken ct) =>
            Task.FromResult<JackpotEstimate?>(null);

        public Task SaveAsync(JackpotEstimate estimate, CancellationToken ct) => Task.CompletedTask;

        public Task<int> CountAsync(Game game, CancellationToken ct) => Task.FromResult(1);
        public Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<Draw>>([]);
        public Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct) => Task.FromResult<DateOnly?>(null);
        public Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct) => Task.FromResult<IReadOnlyList<MatchRow>>([]);
        public Task<bool> UpsertAsync(Draw draw, CancellationToken ct) => Task.FromResult(true);
        public Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct) => Task.CompletedTask;
    }
}
