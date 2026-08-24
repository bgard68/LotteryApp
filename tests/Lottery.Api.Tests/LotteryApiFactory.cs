using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lottery.Api.Tests;

/// <summary>
/// Boots the real API against a throwaway SQLite file: real migrations, real
/// snapshot seed, real endpoint routing and middleware. Only the two things
/// that would reach the internet - the live feeds - and the background refresh
/// loop are swapped out, so a test run is offline and deterministic.
/// </summary>
public class LotteryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"lottery-api-test-{Guid.NewGuid():N}.db");

    /// <summary>Hosting environment to boot under. Several behaviours key off it -
    /// the CSP header, HSTS, and whether the Scalar UI is mapped at all.</summary>
    protected virtual string Environment => "Production";

    /// <summary>Extra configuration applied after the defaults, so a test can override one key.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        builder.ConfigureAppConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Database:Provider"] = "Sqlite",
            };
            foreach (var (key, value) in Settings)
                settings[key] = value;

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // The refresh loop calls live feeds on a timer; a test host has no
            // business doing that. Removing the registration also keeps the
            // test's SQLite file from being written behind the test's back.
            services.RemoveAll<IHostedService>();

            // Feeds are the only outbound network in the app. Fakes here mean
            // /internal/refresh is exercised for real without leaving the box.
            services.RemoveAll<IWinningNumbersFeed>();
            services.AddSingleton<IWinningNumbersFeed, OfflineNumbersFeed>();
            services.RemoveAll<IJackpotFeed>();
            services.AddSingleton<IJackpotFeed, OfflineJackpotFeed>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

/// <summary>The same host booted as Development, where the docs UI is mapped and
/// the strict CSP is deliberately not applied.</summary>
public sealed class DevelopmentApiFactory : LotteryApiFactory
{
    protected override string Environment => "Development";
}

/// <summary>A feed that is reachable and simply has nothing newer to report.</summary>
public sealed class OfflineNumbersFeed : IWinningNumbersFeed
{
    public Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Draw>>([]);
}

/// <summary>A jackpot feed with a fixed estimate, so refresh has something to store.</summary>
public sealed class OfflineJackpotFeed : IJackpotFeed
{
    public const decimal EstimatedJackpot = 100_000_000m;
    public const decimal CashValue = 50_000_000m;

    public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) =>
        Task.FromResult<JackpotInfo?>(new JackpotInfo(
            game,
            LastDrawDate: null,
            LastJackpot: null,
            LastJackpotWon: null,
            NextEstimatedJackpot: EstimatedJackpot,
            NextCashValue: CashValue));
}
