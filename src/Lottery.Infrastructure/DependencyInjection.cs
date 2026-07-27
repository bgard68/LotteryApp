using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Domain;
using Lottery.Infrastructure.Feeds;
using Lottery.Infrastructure.Persistence;
using Lottery.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddTransient<IWinningNumbersFeed>(sp => sp.GetRequiredService<SocrataWinningNumbersFeed>());
        services.AddHttpClient<NyLotteryJackpotFeed>().AddStandardResilienceHandler();
        services.AddHttpClient<PowerballJackpotFeed>().AddStandardResilienceHandler();
        services.AddHttpClient<MegaMillionsJackpotFeed>().AddStandardResilienceHandler();
        services.AddTransient<IJackpotFeed, CompositeJackpotFeed>();

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
