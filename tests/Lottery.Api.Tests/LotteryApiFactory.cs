using System.Net;
using Lottery.Application.Abstractions;
using Lottery.Domain;
using Lottery.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Lottery.Api.Tests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LotteryApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// In-process host over the REAL stack: DbUp migrations, the committed snapshot
/// seed, Dapper over a temp SQLite file, rate limiter, endpoints. Only the two
/// live feeds and the clock are replaced - no test may touch the network, and
/// the clock is pinned to Monday 2026-07-27 noon Eastern so both games' snapshot
/// tails (PB Sat 2026-07-25, MM Fri 2026-07-24) are the current Published draws
/// and every next-draw instant is exactly computable.
/// </summary>
public sealed class LotteryApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset PinnedNowUtc = new(2026, 7, 27, 16, 0, 0, TimeSpan.Zero);

    public static readonly JackpotInfo PowerballJackpot = new(Game.Powerball,
        new DateOnly(2026, 7, 25), 389_000_000m, false, 633_000_000m, 277_300_000m);

    public static readonly JackpotInfo MegaMillionsJackpot = new(Game.MegaMillions,
        new DateOnly(2026, 7, 24), 743_000_000m, false, 800_000_000m, 344_200_000m);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _jackpotDataEnsured;

    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"lottery-api-tests-{Guid.NewGuid():N}.db");

    /// <summary>Never advanced by endpoint tests - a moved clock would shift every expected instant.</summary>
    public FakeTimeProvider Time { get; } = new(PinnedNowUtc);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these while registering services, which happens
        // before WebApplicationFactory's ConfigureAppConfiguration sources are
        // appended - so startup-read keys must ride in host configuration
        // (UseSetting), which exists before the first read. All in-memory
        // requests share one client partition; the shared host is effectively
        // unlimited so throttling is tested only where a derived host opts in.
        builder.UseSetting("RateLimit:PermitPerMinute", "1000000");

        builder.ConfigureTestServices(services =>
        {
            // The connection string is also read at startup, and in Development
            // appsettings.Development.json would win over any config override -
            // so the database is redirected at the service seam instead, with
            // the REAL factory and initializer pointed at the temp file.
            var connectionString = $"Data Source={DbPath}";
            services.RemoveAll<IDbConnectionFactory>();
            services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(connectionString));
            services.RemoveAll<IDatabaseInitializer>();
            services.AddSingleton<IDatabaseInitializer>(sp =>
                new DatabaseInitializer(sp.GetRequiredService<IDbConnectionFactory>(), connectionString));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IWinningNumbersFeed>();
            services.AddSingleton<IWinningNumbersFeed>(new StubWinningNumbersFeed());
            services.RemoveAll<IJackpotFeed>();
            services.AddSingleton<IJackpotFeed>(new StubJackpotFeed(PowerballJackpot, MegaMillionsJackpot));
        });
    }

    /// <summary>
    /// The startup gap-repair writes jackpot figures on a background thread, so
    /// tests asserting dollar amounts run one refresh cycle over HTTP first -
    /// after this returns, the estimates and draw stamps are durably stored.
    /// </summary>
    public async Task EnsureJackpotDataAsync(HttpClient client)
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (_jackpotDataEnsured)
                return;

            using var response = await client.PostAsync("/internal/refresh", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _jackpotDataEnsured = true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(DbPath))
                    File.Delete(DbPath);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a straggling handle is harmless here.
            }
        }
    }
}
