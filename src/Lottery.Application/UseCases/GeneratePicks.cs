using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record GeneratedTicket(IReadOnlyList<int> WhiteBalls, int Special);

public sealed record GeneratedPicks(Game Game, IReadOnlyList<GeneratedTicket> Tickets);

public sealed class GeneratePicks(IPickGenerator generator, TimeProvider time)
{
    public const int MinCount = 1;
    public const int MaxCount = 10;

    public GeneratedPicks Execute(Game game, int count = 1)
    {
        if (count is < MinCount or > MaxCount)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"count must be between {MinCount} and {MaxCount}.");

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var era = RuleEras.Current(game, today);

        var tickets = new List<GeneratedTicket>(count);
        for (var i = 0; i < count; i++)
        {
            var (whites, special) = generator.Generate(era);
            tickets.Add(new GeneratedTicket(whites, special));
        }

        return new GeneratedPicks(game, tickets);
    }
}
