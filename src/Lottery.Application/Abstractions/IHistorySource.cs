using Lottery.Domain;

namespace Lottery.Application.Abstractions;

/// <summary>
/// Source of historical winning numbers. Phase 1 implementation reads a committed
/// snapshot (offline, deterministic); the live NY Open Data feed implements the
/// same port for gap-repair and snapshot refresh.
/// </summary>
public interface IHistorySource
{
    string Name { get; }
    Task<IReadOnlyList<Draw>> GetHistoryAsync(Game game, CancellationToken ct);
}
