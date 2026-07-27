using Lottery.Domain;

namespace Lottery.Application.Abstractions;

public sealed record ImportRecord(Game Game, string Source, DateTimeOffset CompletedAtUtc,
    int DrawCount, DateOnly EarliestDraw, DateOnly LatestDraw);

/// <summary>Records completed one-time imports so they never run twice.</summary>
public interface IImportLedger
{
    Task<ImportRecord?> GetAsync(Game game, CancellationToken ct);
    Task RecordAsync(ImportRecord record, CancellationToken ct);
}
