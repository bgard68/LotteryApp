namespace Lottery.Domain;

/// <summary>
/// A period during which a game's number matrix was constant. Lotteries change
/// their ranges over time, so validation of any draw or ticket must be against
/// the era in force on its draw date.
/// </summary>
public sealed record RuleEra(Game Game, DateOnly EffectiveFrom, int WhiteBallMax, int SpecialBallMax)
{
    public const int WhiteBallCount = 5;

    public bool IsValidWhite(int n) => n >= 1 && n <= WhiteBallMax;
    public bool IsValidSpecial(int n) => n >= 1 && n <= SpecialBallMax;

    public bool IsValidDraw(IReadOnlyCollection<int> whites, int special) =>
        whites.Count == WhiteBallCount
        && whites.Distinct().Count() == WhiteBallCount
        && whites.All(IsValidWhite)
        && IsValidSpecial(special);
}
