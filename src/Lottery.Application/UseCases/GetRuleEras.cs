using Lottery.Domain;

namespace Lottery.Application.UseCases;

public sealed record RuleEraDto(DateOnly EffectiveFrom, int WhiteBallMax, int WhiteBallCount, int SpecialBallMax, bool IsCurrent);

public sealed class GetRuleEras(TimeProvider time)
{
    public IReadOnlyList<RuleEraDto> Execute(Game game)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var current = RuleEras.Current(game, today);

        return RuleEras.All
            .Where(e => e.Game == game)
            .OrderBy(e => e.EffectiveFrom)
            .Select(e => new RuleEraDto(e.EffectiveFrom, e.WhiteBallMax, RuleEra.WhiteBallCount, e.SpecialBallMax, e == current))
            .ToList();
    }
}
