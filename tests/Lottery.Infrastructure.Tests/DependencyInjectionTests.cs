using Lottery.Application.Abstractions;
using Lottery.Application.UseCases;
using Lottery.Infrastructure.Feeds;
using Lottery.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// The composition root. Provider selection is configuration-driven and decided
/// once at startup, so a mistake here is the difference between the app running
/// on Azure SQL and the app quietly writing to a local file that vanishes with
/// the container.
/// </summary>
public class DependencyInjectionTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    // AddInfrastructure takes the configuration AND expects one in the container:
    // SocrataWinningNumbersFeed is a typed HttpClient activated by the container,
    // and reads its app token from IConfiguration. The web host registers that
    // itself, so this mirrors what Program.cs ends up with.
    private static ServiceProvider Build(IConfiguration configuration) =>
        new ServiceCollection()
            .AddSingleton(configuration)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

    [Fact]
    public void WithNoConfiguration_DefaultsToSqlite()
    {
        using var provider = Build(Config());

        Assert.IsType<SqliteConnectionFactory>(provider.GetRequiredService<IDbConnectionFactory>());
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("sqlserver")] // configuration casing is not something we control
    public void SqlServerProvider_UsesTheSqlServerFactory(string provider)
    {
        using var services = Build(Config(
            ("Database:Provider", provider),
            ("ConnectionStrings:Default", "Server=tcp:x.database.windows.net,1433;Database=lottery;Encrypt=True")));

        var factory = services.GetRequiredService<IDbConnectionFactory>();
        Assert.IsType<SqlServerConnectionFactory>(factory);
        Assert.Equal(SqlDialect.SqlServer, factory.Dialect);
    }

    [Fact]
    public void SqlServerProvider_WithoutAConnectionString_FailsAtStartup()
    {
        // Falling back to the SQLite default file here would look like a healthy
        // boot while every write went to disposable container storage.
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build(Config(("Database:Provider", "SqlServer"))));

        Assert.Contains("ConnectionStrings:Default", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqliteProvider_UsesTheConfiguredConnectionString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lottery-di-test-{Guid.NewGuid():N}.db");
        using var services = Build(Config(
            ("Database:Provider", "Sqlite"),
            ("ConnectionStrings:Default", $"Data Source={path}")));

        var factory = services.GetRequiredService<IDbConnectionFactory>();
        Assert.IsType<SqliteConnectionFactory>(factory);
        Assert.Equal(SqlDialect.Sqlite, factory.Dialect);
    }

    [Fact]
    public void TheJackpotFeedIsTheCompositeOne()
    {
        // The individual feeds are registered as typed HttpClients, but only the
        // composite is bound to IJackpotFeed - resolving anything else would skip
        // the Powerball fallback entirely.
        using var provider = Build(Config());

        Assert.IsType<CompositeJackpotFeed>(provider.GetRequiredService<IJackpotFeed>());
        Assert.IsType<SocrataWinningNumbersFeed>(provider.GetRequiredService<IWinningNumbersFeed>());
    }

    [Fact]
    public void EveryUseCaseResolves()
    {
        using var provider = Build(Config());

        Assert.NotNull(provider.GetRequiredService<RefreshGame>());
        Assert.NotNull(provider.GetRequiredService<ImportHistory>());
        Assert.NotNull(provider.GetRequiredService<CheckTicket>());
        Assert.NotNull(provider.GetRequiredService<GeneratePicks>());
        Assert.NotNull(provider.GetRequiredService<GetDraws>());
        Assert.NotNull(provider.GetRequiredService<GetLatestDraw>());
        Assert.NotNull(provider.GetRequiredService<GetNextDraw>());
        Assert.NotNull(provider.GetRequiredService<GetRuleEras>());
        Assert.NotNull(provider.GetRequiredService<IDatabaseInitializer>());
        Assert.NotNull(provider.GetRequiredService<IHistorySource>());
        Assert.NotNull(provider.GetRequiredService<IImportLedger>());
        Assert.NotNull(provider.GetRequiredService<IJackpotStore>());
        Assert.NotNull(provider.GetRequiredService<IDrawRepository>());
    }

    [Fact]
    public void TypedHttpClientFeedsAreNotSingletons()
    {
        // A typed HttpClient captured by a singleton pins one handler forever and
        // stops DNS rotation - RefreshGame is registered transient for this reason.
        using var provider = Build(Config());

        Assert.NotSame(provider.GetRequiredService<RefreshGame>(), provider.GetRequiredService<RefreshGame>());
        Assert.NotSame(provider.GetRequiredService<IJackpotFeed>(), provider.GetRequiredService<IJackpotFeed>());
    }
}
