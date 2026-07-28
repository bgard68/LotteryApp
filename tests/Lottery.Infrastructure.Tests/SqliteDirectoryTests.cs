using Lottery.Infrastructure.Persistence;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Regression coverage for the production crash loop: SQLite creates database
/// FILES but never DIRECTORIES, so a connection string pointing into a folder
/// that does not exist yet throws on every attempt. In Azure that restarted the
/// container 51 times and exhausted the plan's daily CPU quota.
///
/// Every existing test used a path in the temp directory, which always exists -
/// which is exactly why nothing caught it. These tests use paths that do not.
/// </summary>
public sealed class SqliteDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lottery-dirtest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Opens_a_database_in_a_directory_that_does_not_exist_yet()
    {
        // Mirrors production: /home/data/lottery.db where /home/data is absent.
        var path = Path.Combine(_root, "data", "lottery.db");
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));

        var factory = new SqliteConnectionFactory($"Data Source={path}");
        await using var connection = await factory.OpenAsync(CancellationToken.None);

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Migrations_run_against_a_nested_missing_path()
    {
        // The full startup path - directory creation, then DbUp - is what
        // actually failed in Azure, so exercise both together.
        var path = Path.Combine(_root, "deeply", "nested", "lottery.db");
        var connectionString = $"Data Source={path}";

        var factory = new SqliteConnectionFactory(connectionString);
        new DatabaseInitializer(factory, connectionString).Initialize();

        var repository = new DrawRepository(factory);
        Assert.Equal(0, await repository.CountAsync(Lottery.Domain.Game.Powerball, CancellationToken.None));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=lottery.db")] // relative: no directory component
    public void Handles_paths_with_no_directory_to_create(string connectionString)
    {
        // Must not throw while trying to create a directory that isn't there.
        SqliteConnectionFactory.EnsureDirectoryExists(connectionString);
    }
}
