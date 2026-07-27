namespace Lottery.Domain;

/// <summary>
/// A published drawing. White balls are stored sorted ascending; order of draw
/// is irrelevant to matching and official displays sort them the same way.
/// </summary>
public sealed record Draw
{
    public required Game Game { get; init; }
    public required DateOnly DrawDate { get; init; }
    public required IReadOnlyList<int> WhiteBalls { get; init; }
    public required int Special { get; init; }
    public decimal? JackpotAmount { get; init; }
    public bool? JackpotWon { get; init; }

    // Records compare collection properties by reference; draws are equal when
    // their values are, so white balls compare by content.
    public bool Equals(Draw? other) =>
        other is not null
        && Game == other.Game
        && DrawDate == other.DrawDate
        && WhiteBalls.SequenceEqual(other.WhiteBalls)
        && Special == other.Special
        && JackpotAmount == other.JackpotAmount
        && JackpotWon == other.JackpotWon;

    public override int GetHashCode() => HashCode.Combine(Game, DrawDate, Special);

    public static Draw Create(Game game, DateOnly drawDate, IEnumerable<int> whiteBalls, int special,
        decimal? jackpotAmount = null, bool? jackpotWon = null)
    {
        var whites = whiteBalls.OrderBy(n => n).ToArray();
        if (whites.Length != 5)
            throw new ArgumentException($"Expected 5 white balls, got {whites.Length}.", nameof(whiteBalls));
        if (whites.Distinct().Count() != 5)
            throw new ArgumentException("White balls must be distinct.", nameof(whiteBalls));

        return new Draw
        {
            Game = game,
            DrawDate = drawDate,
            WhiteBalls = whites,
            Special = special,
            JackpotAmount = jackpotAmount,
            JackpotWon = jackpotWon,
        };
    }
}
