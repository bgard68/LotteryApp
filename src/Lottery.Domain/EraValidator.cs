namespace Lottery.Domain;

public sealed record EraViolation(Game Game, DateOnly DrawDate, string Reason);

/// <summary>
/// Defensive layer for rule changes: validates draws against the known-era table.
/// Run over the full imported history by tests (and weekly in CI), any violation
/// means either bad feed data or an undocumented rule change - both must fail loudly.
/// </summary>
public static class EraValidator
{
    public static EraViolation? Validate(Draw draw)
    {
        // A date no era covers is a violation like any other, and reporting it as one is the
        // point. This used to call RuleEras.ForDate, which THROWS for an uncovered date - so a
        // validator whose job is to return violations signalled one kind of invalidity by
        // throwing and the rest by returning. Callers had to both check the result and guard the
        // call, and RefreshGame's catch filter carried ArgumentException solely for this: one
        // bogus feed date escaped the method entirely and answered 500.
        if (!RuleEras.TryForDate(draw.Game, draw.DrawDate, out var era))
            return new EraViolation(draw.Game, draw.DrawDate,
                $"No known {draw.Game} rule era covers {draw.DrawDate:yyyy-MM-dd}.");

        if (!draw.WhiteBalls.All(era.IsValidWhite))
            return new EraViolation(draw.Game, draw.DrawDate,
                $"White balls [{string.Join(",", draw.WhiteBalls)}] outside 1-{era.WhiteBallMax} (era from {era.EffectiveFrom:yyyy-MM-dd}).");

        if (!era.IsValidSpecial(draw.Special))
            return new EraViolation(draw.Game, draw.DrawDate,
                $"Special ball {draw.Special} outside 1-{era.SpecialBallMax} (era from {era.EffectiveFrom:yyyy-MM-dd}).");

        return null;
    }

    public static IReadOnlyList<EraViolation> ValidateHistory(IEnumerable<Draw> draws) =>
        draws.Select(Validate).Where(v => v is not null).Cast<EraViolation>().ToList();
}
