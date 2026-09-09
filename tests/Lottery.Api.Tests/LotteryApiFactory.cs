using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Persistence;
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
    /// <summary>The throwaway database this host must use - asserted by the
    /// factory's own sentinel test, because a silently ignored override would
    /// run every API test against a shared default-path file.</summary>
    public string DbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"lottery-api-test-{Guid.NewGuid():N}.db");

    /// <summary>Hosting environment to boot under. Several behaviours key off it -
    /// the CSP header, HSTS, and whether the Scalar UI is mapped at all.</summary>
    protected virtual string Environment => "Production";

    /// <summary>Extra configuration applied after the defaults, so a test can
    /// override one key. Only keys the app reads at request time land here in
    /// time; startup-read keys need UseSetting (see the connection string below).</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        builder.ConfigureAppConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={DbPath}",
                ["Database:Provider"] = "Sqlite",
            };
            foreach (var (key, value) in Settings)
                settings[key] = value;

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // The connection string is consumed while services are being
            // registered - BEFORE the in-memory configuration above is appended
            // - so the config entry alone silently loses to the "lottery.db"
            // fallback and every factory shares one file in the test bin
            // (lesson 31). Redirecting at the service seam, with the real
            // factory and initializer, is timing-proof.
            var connectionString = $"Data Source={DbPath}";
            services.RemoveAll<IDbConnectionFactory>();
            services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(connectionString));
            services.RemoveAll<IDatabaseInitializer>();
            services.AddSingleton<IDatabaseInitializer>(sp =>
                new DatabaseInitializer(sp.GetRequiredService<IDbConnectionFactory>(), connectionString));

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
        if (File.Exists(DbPath)) File.Delete(DbPath);
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
