using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record ImportSummary(Game Game, bool Skipped, int DrawCount);

/// <summary>
/// One-time historical import. The ledger guards re-runs; every draw is validated
/// against its rule era before anything is written, so bad data or an unknown
/// rule change aborts the import instead of poisoning the database.
/// </summary>
public sealed class ImportHistory(IDrawRepository draws, IImportLedger ledger, IHistorySource source, TimeProvider time)
{
    public async Task<ImportSummary> ExecuteAsync(Game game, CancellationToken ct)
    {
        if (await ledger.GetAsync(game, ct) is not null)
            return new ImportSummary(game, Skipped: true, 0);

        var history = await source.GetHistoryAsync(game, ct);
        if (history.Count == 0)
            throw new InvalidOperationException($"History source '{source.Name}' returned no draws for {game}.");

        var violations = EraValidator.ValidateHistory(history);
        if (violations.Count > 0)
            throw new InvalidOperationException(
                $"{violations.Count} era violation(s) in {game} history; first: {violations[0].Reason}");

        await draws.BulkInsertAsync(history, ct);

        await ledger.RecordAsync(new ImportRecord(
            game,
            source.Name,
            time.GetUtcNow(),
            history.Count,
            history.Min(d => d.DrawDate),
            history.Max(d => d.DrawDate)), ct);

        return new ImportSummary(game, Skipped: false, history.Count);
    }
}
