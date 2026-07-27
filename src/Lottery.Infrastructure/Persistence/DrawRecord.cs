using Lottery.Domain;

namespace Lottery.Infrastructure.Persistence;

/// <summary>DB-shaped row; dates travel as ISO-8601 strings so both dialects map identically.</summary>
public sealed class DrawRecord
{
    public string Game { get; set; } = "";
    public string DrawDate { get; set; } = "";
    public int White1 { get; set; }
    public int White2 { get; set; }
    public int White3 { get; set; }
    public int White4 { get; set; }
    public int White5 { get; set; }
    public int Special { get; set; }
    public decimal? JackpotAmount { get; set; }
    public bool? JackpotWon { get; set; }

    public Draw ToDomain() => Draw.Create(
        Enum.Parse<Game>(Game),
        DateOnly.Parse(DrawDate),
        [White1, White2, White3, White4, White5],
        Special,
        JackpotAmount,
        JackpotWon);

    public static object ToParams(Draw draw) => new
    {
        Game = draw.Game.ToString(),
        DrawDate = draw.DrawDate.ToString("yyyy-MM-dd"),
        White1 = draw.WhiteBalls[0],
        White2 = draw.WhiteBalls[1],
        White3 = draw.WhiteBalls[2],
        White4 = draw.WhiteBalls[3],
        White5 = draw.WhiteBalls[4],
        draw.Special,
        draw.JackpotAmount,
        draw.JackpotWon,
    };
}
