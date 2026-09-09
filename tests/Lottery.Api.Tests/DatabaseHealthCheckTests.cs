using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lottery.Api.Tests;

/// <summary>
/// The health check is what the deploy gate, the keep-alive workflow and Azure
/// all watch, so each of its three verdicts is pinned: a reachable seeded
/// database, a reachable empty one, and one that throws.
/// </summary>
public sealed class DatabaseHealthCheckTests
{
    private static Task<HealthCheckResult> CheckAsync(IDrawRepository repository)
    {
        var check = new DatabaseHealthCheck(repository);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", check, failureStatus: null, tags: null),
        };

        return check.CheckHealthAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task SeededDatabase_IsHealthy_AndReportsTheRowCount()
    {
        var result = await CheckAsync(new StubDrawCounter(42));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("42", result.Description);
    }

    [Fact]
    public async Task EmptyDatabase_IsDegraded_BecauseSeedingDidNotComplete()
    {
        // Reachable but empty is the signature of a half-finished startup, and
        // it must not read as Healthy - the instance would serve empty results.
        var result = await CheckAsync(new StubDrawCounter(0));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("seeding", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnreachableDatabase_IsUnhealthy_AndKeepsTheException()
    {
        var boom = new InvalidOperationException("no such table: draws");

        var result = await CheckAsync(new ThrowingDrawRepository(boom));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Same(boom, result.Exception);
    }

    [Fact]
    public async Task TheCheckAsksAboutPowerball_TheGameTheSeedAlwaysPopulates()
    {
        var counter = new StubDrawCounter(1);

        await CheckAsync(counter);

        Assert.Equal(Game.Powerball, counter.LastGame);
    }

    private sealed class StubDrawCounter(int count) : NotUsedDrawRepository
    {
        public Game? LastGame { get; private set; }

        public override Task<int> CountAsync(Game game, CancellationToken ct)
        {
            LastGame = game;
            return Task.FromResult(count);
        }
    }

    private sealed class ThrowingDrawRepository(Exception exception) : NotUsedDrawRepository
    {
        public override Task<int> CountAsync(Game game, CancellationToken ct) => throw exception;
    }
}

/// <summary>
/// The health check touches exactly one repository method. Everything else
/// throws rather than returning a plausible empty value, so a future change
/// that starts calling something new fails loudly instead of passing quietly.
/// </summary>
public abstract class NotUsedDrawRepository : IDrawRepository
{
    public virtual Task<int> CountAsync(Game game, CancellationToken ct) => throw new NotSupportedException();
    public Task<Draw?> GetLatestAsync(Game game, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> UpsertAsync(Draw draw, CancellationToken ct) => throw new NotSupportedException();
    public Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct) => throw new NotSupportedException();
    public Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct) => throw new NotSupportedException();
}
