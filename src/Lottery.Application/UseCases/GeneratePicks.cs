using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record GeneratedPicks(Game Game, IReadOnlyList<int> WhiteBalls, int Special);

public sealed class GeneratePicks(IPickGenerator generator, TimeProvider time)
{
    public GeneratedPicks Execute(Game game)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var era = RuleEras.Current(game, today);
        var (whites, special) = generator.Generate(era);
        return new GeneratedPicks(game, whites, special);
    }
}
