using Dapper;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Persistence;

public sealed class ImportLedgerRepository(IDbConnectionFactory factory) : IImportLedger
{
    public async Task<ImportRecord?> GetAsync(Game game, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<LedgerRow>(new CommandDefinition(
            "SELECT Game, Source, CompletedAtUtc, DrawCount, EarliestDraw, LatestDraw FROM ImportLedger WHERE Game = @game",
            new { game = game.ToString() }, cancellationToken: ct));

        return row is null
            ? null
            : new ImportRecord(
                Enum.Parse<Game>(row.Game),
                row.Source,
                DateTimeOffset.Parse(row.CompletedAtUtc),
                row.DrawCount,
                DateOnly.Parse(row.EarliestDraw.AsSpan(0, 10)),
                DateOnly.Parse(row.LatestDraw.AsSpan(0, 10)));
    }

    public async Task RecordAsync(ImportRecord record, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ImportLedger (Game, Source, CompletedAtUtc, DrawCount, EarliestDraw, LatestDraw) " +
            "VALUES (@Game, @Source, @CompletedAtUtc, @DrawCount, @EarliestDraw, @LatestDraw)",
            new
            {
                Game = record.Game.ToString(),
                record.Source,
                CompletedAtUtc = record.CompletedAtUtc.ToString("O"),
                record.DrawCount,
                EarliestDraw = record.EarliestDraw.ToString("yyyy-MM-dd"),
                LatestDraw = record.LatestDraw.ToString("yyyy-MM-dd"),
            }, cancellationToken: ct));
    }

    private sealed class LedgerRow
    {
        public string Game { get; set; } = "";
        public string Source { get; set; } = "";
        public string CompletedAtUtc { get; set; } = "";
        public int DrawCount { get; set; }
        public string EarliestDraw { get; set; } = "";
        public string LatestDraw { get; set; } = "";
    }
}
