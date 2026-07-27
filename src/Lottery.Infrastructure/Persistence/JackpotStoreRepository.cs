using Dapper;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Persistence;

public sealed class JackpotStoreRepository(IDbConnectionFactory factory) : IJackpotStore
{
    public async Task<JackpotEstimate?> GetAsync(Game game, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT Game, NextEstimatedJackpot, NextCashValue, UpdatedAtUtc FROM JackpotEstimates WHERE Game = @game",
            new { game = game.ToString() }, cancellationToken: ct));

        return row is null
            ? null
            : new JackpotEstimate(Enum.Parse<Game>(row.Game), row.NextEstimatedJackpot,
                row.NextCashValue, DateTimeOffset.Parse(row.UpdatedAtUtc));
    }

    public async Task SaveAsync(JackpotEstimate estimate, CancellationToken ct)
    {
        var sql = factory.Dialect == SqlDialect.Sqlite
            ? "INSERT INTO JackpotEstimates (Game, NextEstimatedJackpot, NextCashValue, UpdatedAtUtc) " +
              "VALUES (@Game, @NextEstimatedJackpot, @NextCashValue, @UpdatedAtUtc) " +
              "ON CONFLICT (Game) DO UPDATE SET NextEstimatedJackpot = @NextEstimatedJackpot, " +
              "NextCashValue = @NextCashValue, UpdatedAtUtc = @UpdatedAtUtc"
            : "MERGE JackpotEstimates AS t USING (SELECT @Game AS Game) AS s ON t.Game = s.Game " +
              "WHEN MATCHED THEN UPDATE SET NextEstimatedJackpot = @NextEstimatedJackpot, " +
              "NextCashValue = @NextCashValue, UpdatedAtUtc = @UpdatedAtUtc " +
              "WHEN NOT MATCHED THEN INSERT (Game, NextEstimatedJackpot, NextCashValue, UpdatedAtUtc) " +
              "VALUES (@Game, @NextEstimatedJackpot, @NextCashValue, @UpdatedAtUtc);";

        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Game = estimate.Game.ToString(),
            estimate.NextEstimatedJackpot,
            estimate.NextCashValue,
            UpdatedAtUtc = estimate.UpdatedAtUtc.ToString("O"),
        }, cancellationToken: ct));
    }

    private sealed class Row
    {
        public string Game { get; set; } = "";
        public decimal? NextEstimatedJackpot { get; set; }
        public decimal? NextCashValue { get; set; }
        public string UpdatedAtUtc { get; set; } = "";
    }
}
