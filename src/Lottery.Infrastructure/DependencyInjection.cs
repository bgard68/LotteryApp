using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Lottery.Infrastructure.Persistence;
using Lottery.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lottery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Provider chosen by config: SQLite locally, Azure SQL (SqlServer) in production.
        var provider = configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
                ? "Data Source=lottery.db"
                : throw new InvalidOperationException(
                    "ConnectionStrings:Default is required when Database:Provider is SqlServer."));

        services.AddSingleton<IDbConnectionFactory>(
            provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? new SqlServerConnectionFactory(connectionString)
                : new SqliteConnectionFactory(connectionString));

        services.AddSingleton<IDatabaseInitializer>(sp =>
            new DatabaseInitializer(sp.GetRequiredService<IDbConnectionFactory>(), connectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPickGenerator>(new RandomPickGenerator());

        services.AddSingleton<IDrawRepository, DrawRepository>();
        services.AddSingleton<IImportLedger, ImportLedgerRepository>();
        services.AddSingleton<IHistorySource, SnapshotHistorySource>();
        services.AddSingleton<IJackpotStore, JackpotStoreRepository>();

        // Live feeds: typed HttpClients with the standard resilience pipeline
        // (retry + circuit breaker + timeout). Registered transient - typed
        // clients must not be captured by singletons or handler rotation stops.
        services.AddHttpClient<SocrataWinningNumbersFeed>().AddStandardResilienceHandler();
        services.AddHttpClient<NyLotteryJackpotFeed>().AddStandardResilienceHandler();
        services.AddHttpClient<PowerballJackpotFeed>().AddStandardResilienceHandler();
        services.AddHttpClient<MegaMillionsJackpotFeed>().AddStandardResilienceHandler();

        // The port the Application sees is the adapter wrapped in the fault-tolerance decorator,
        // so the "report, never throw" contract holds for whatever is inside - including the
        // resilience pipeline's own BrokenCircuitException and TimeoutRejectedException, which no
        // hand-written exception allowlist had ever named.
        services.AddTransient<IWinningNumbersFeed>(sp => new FaultTolerantWinningNumbersFeed(
            sp.GetRequiredService<SocrataWinningNumbersFeed>(),
            sp.GetRequiredService<ILogger<FaultTolerantWinningNumbersFeed>>()));

        // Jackpot sources in priority order; CompositeJackpotFeed asks each in turn and takes the
        // first that answers. Each one declines the games it does not serve, so this ORDER is the
        // whole routing policy: Mega Millions is served only by megamillions.com, and Powerball
        // prefers the NY Lottery API with the retired powerball.com endpoint as a fallback.
        // Adding a source means adding a line here.
        services.AddTransient<IJackpotFeed>(sp => new FaultTolerantJackpotFeed(
            new CompositeJackpotFeed(
                [
                    sp.GetRequiredService<MegaMillionsJackpotFeed>(),
                    sp.GetRequiredService<NyLotteryJackpotFeed>(),
                    sp.GetRequiredService<PowerballJackpotFeed>(),
                ],
                sp.GetRequiredService<ILogger<CompositeJackpotFeed>>()),
            sp.GetRequiredService<ILogger<FaultTolerantJackpotFeed>>()));

        services.AddSingleton<GetNextDraw>();
        services.AddSingleton<GetLatestDraw>();
        services.AddSingleton<GetDraws>();
        services.AddSingleton<CheckTicket>();
        services.AddSingleton<GeneratePicks>();
        services.AddSingleton<GetRuleEras>();
        services.AddSingleton<ImportHistory>();
        services.AddTransient<RefreshGame>(); // transient: depends on typed HttpClient feeds

        return services;
    }
}
