using System.Reflection;
using System.Text.Json;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Seeding;

/// <summary>
/// Reads the committed history snapshots (embedded JSON, captured from NY Open Data).
/// Keeps first-boot seeding offline and deterministic; the live feed handles
/// everything after the snapshot's last date.
/// </summary>
public sealed class SnapshotHistorySource : IHistorySource
{
    public string Name => "snapshot:data.ny.gov";

    public Task<IReadOnlyList<Draw>> GetHistoryAsync(Game game, CancellationToken ct)
    {
        var resource = game == Game.Powerball ? "powerball-history.json" : "megamillions-history.json";
        var assembly = typeof(SnapshotHistorySource).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(resource, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded snapshot '{resource}' not found.");

        var rows = JsonSerializer.Deserialize<List<SnapshotRow>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Snapshot '{resource}' deserialized to null.");

        IReadOnlyList<Draw> draws = rows
            .Select(r => Draw.Create(game, DateOnly.Parse(r.Date), r.Whites, r.Special))
            .OrderBy(d => d.DrawDate)
            .ToList();

        return Task.FromResult(draws);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SnapshotRow
    {
        public string Date { get; set; } = "";
        public int[] Whites { get; set; } = [];
        public int Special { get; set; }
    }
}
