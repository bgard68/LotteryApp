using Dapper;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Persistence;

public sealed class DrawRepository(IDbConnectionFactory factory) : IDrawRepository
{
    private const string Columns =
        "Game, DrawDate, White1, White2, White3, White4, White5, Special, JackpotAmount, JackpotWon";

    public async Task<Draw?> GetLatestAsync(Game game, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<DrawRecord>(new CommandDefinition(
            $"SELECT {Columns} FROM Draws WHERE Game = @game ORDER BY DrawDate DESC LIMIT 1"
                .AdaptLimit(factory.Dialect),
            new { game = game.ToString() }, cancellationToken: ct));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<DrawRecord>(new CommandDefinition(
            ($"SELECT {Columns} FROM Draws WHERE Game = @game " +
             "AND (@from IS NULL OR DrawDate >= @from) AND (@to IS NULL OR DrawDate <= @to) " +
             "ORDER BY DrawDate DESC LIMIT @limit").AdaptLimit(factory.Dialect),
            new
            {
                game = game.ToString(),
                from = from?.ToString("yyyy-MM-dd"),
                to = to?.ToString("yyyy-MM-dd"),
                limit,
            }, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<int> CountAsync(Game game, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM Draws WHERE Game = @game",
            new { game = game.ToString() }, cancellationToken: ct));
    }

    public async Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var value = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT MIN(DrawDate) FROM Draws WHERE Game = @game",
            new { game = game.ToString() }, cancellationToken: ct));
        return value is null ? null : DateOnly.Parse(value.AsSpan(0, 10));
    }

    public async Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct)
    {
        // Set-wise white matching in SQL: each stored column tested against the
        // ticket's five values. Order-independent by construction.
        const string sql = """
            SELECT DrawDate, White1, White2, White3, White4, White5, Special,
                   (CASE WHEN White1 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                  + CASE WHEN White2 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                  + CASE WHEN White3 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                  + CASE WHEN White4 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                  + CASE WHEN White5 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END) AS WhiteMatches,
                   CASE WHEN Special = @special THEN 1 ELSE 0 END AS SpecialMatched
            FROM Draws
            WHERE Game = @game
              AND (Special = @special
                   OR (CASE WHEN White1 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                     + CASE WHEN White2 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                     + CASE WHEN White3 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                     + CASE WHEN White4 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END
                     + CASE WHEN White5 IN (@w1,@w2,@w3,@w4,@w5) THEN 1 ELSE 0 END) >= 3)
            """;

        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<(string DrawDate, int White1, int White2, int White3, int White4, int White5,
            int Special, int WhiteMatches, int SpecialMatched)>(new CommandDefinition(
            sql,
            new
            {
                game = game.ToString(),
                w1 = whites[0], w2 = whites[1], w3 = whites[2], w4 = whites[3], w5 = whites[4],
                special,
            }, cancellationToken: ct));

        return rows
            .Select(r => new MatchRow(
                DateOnly.Parse(r.DrawDate.AsSpan(0, 10)),
                [r.White1, r.White2, r.White3, r.White4, r.White5],
                r.Special,
                r.WhiteMatches,
                r.SpecialMatched == 1))
            .ToList();
    }

    public async Task<bool> UpsertAsync(Draw draw, CancellationToken ct)
    {
        var sql = factory.Dialect == SqlDialect.Sqlite
            ? $"INSERT INTO Draws ({Columns}) VALUES (@Game, @DrawDate, @White1, @White2, @White3, @White4, @White5, @Special, @JackpotAmount, @JackpotWon) " +
              "ON CONFLICT (Game, DrawDate) DO NOTHING"
            : $"INSERT INTO Draws ({Columns}) SELECT @Game, @DrawDate, @White1, @White2, @White3, @White4, @White5, @Special, @JackpotAmount, @JackpotWon " +
              "WHERE NOT EXISTS (SELECT 1 FROM Draws WHERE Game = @Game AND DrawDate = @DrawDate)";

        await using var conn = await factory.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql, DrawRecord.ToParams(draw), cancellationToken: ct));
        return affected > 0;
    }

    public async Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE Draws SET JackpotAmount = @jackpotAmount, JackpotWon = @jackpotWon " +
            "WHERE Game = @game AND DrawDate = @drawDate",
            new
            {
                game = game.ToString(),
                drawDate = drawDate.ToString("yyyy-MM-dd"),
                jackpotAmount,
                jackpotWon,
            }, cancellationToken: ct));
    }

    public async Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var sql = $"INSERT INTO Draws ({Columns}) VALUES (@Game, @DrawDate, @White1, @White2, @White3, @White4, @White5, @Special, @JackpotAmount, @JackpotWon)";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, draws.Select(DrawRecord.ToParams), transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }
}

internal static class SqlDialectExtensions
{
    /// <summary>SQLite uses LIMIT @n; SQL Server uses TOP/OFFSET-FETCH.</summary>
    public static string AdaptLimit(this string sql, SqlDialect dialect) =>
        dialect == SqlDialect.Sqlite
            ? sql
            : sql.Replace("LIMIT @limit", "OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY", StringComparison.Ordinal)
                 .Replace("LIMIT 1", "OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", StringComparison.Ordinal);
}
