using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed class GetDraws(IDrawRepository draws)
{
    public const int MaxLimit = 200;

    public Task<IReadOnlyList<Draw>> ExecuteAsync(Game game, DateOnly? from, DateOnly? to, int? limit, CancellationToken ct)
    {
        var capped = Math.Clamp(limit ?? 50, 1, MaxLimit);
        return draws.GetRangeAsync(game, from, to, capped, ct);
    }
}
