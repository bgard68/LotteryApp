using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lottery.Api;

public static class LotteryEndpoints
{
    public static void MapLotteryEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/{game}");

        api.MapGet("/next-draw", (string game, GetNextDraw useCase) =>
            WithGame(game, g =>
            {
                var result = useCase.Execute(g);
                return Results.Ok(new
                {
                    game = g.ToString(),
                    drawDate = result.DrawDate,
                    drawTimeUtc = result.DrawTimeUtc,
                });
            }));

        api.MapGet("/latest", async (string game, GetLatestDraw useCase, CancellationToken ct) =>
            await WithGameAsync(game, async g =>
            {
                var result = await useCase.ExecuteAsync(g, ct);
                return Results.Ok(new
                {
                    game = g.ToString(),
                    status = result!.Status.ToString(),
                    drawDate = result.DrawDate,
                    whiteBalls = result.WhiteBalls,
                    special = result.Special,
                    specialName = g.SpecialBallName(),
                    jackpotAmount = result.JackpotAmount,
                    jackpotWon = result.JackpotWon,
                });
            }));

        api.MapGet("/draws", async (string game, DateOnly? from, DateOnly? to, int? limit,
                GetDraws useCase, CancellationToken ct) =>
            await WithGameAsync(game, async g =>
            {
                var draws = await useCase.ExecuteAsync(g, from, to, limit, ct);
                return Results.Ok(draws.Select(d => new
                {
                    drawDate = d.DrawDate,
                    whiteBalls = d.WhiteBalls,
                    special = d.Special,
                }));
            }));

        // GET on purpose: a check reads data and changes nothing, so it is
        // cacheable and smoke-testable with a plain URL.
        api.MapGet("/check", async (string game, string? whites, int? special,
                CheckTicket useCase, CancellationToken ct) =>
            await WithGameAsync(game, async g =>
            {
                if (whites is null || special is null)
                    return Results.BadRequest(new { error = "Query parameters 'whites' (5 comma-separated numbers) and 'special' are required." });

                var parts = whites.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var parsed = new List<int>(parts.Length);
                foreach (var p in parts)
                {
                    if (!int.TryParse(p, out var n))
                        return Results.BadRequest(new { error = $"'{p}' is not a number." });
                    parsed.Add(n);
                }

                var result = await useCase.ExecuteAsync(g, parsed, special.Value, ct);
                return result.Status switch
                {
                    CheckStatus.InvalidTicket => Results.BadRequest(new { error = result.Error }),
                    CheckStatus.DataUnavailable => Results.Json(
                        new { status = "DataUnavailable", error = result.Error },
                        statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => Results.Ok(new
                    {
                        status = "Ok",
                        drawsChecked = result.DrawsChecked,
                        historySince = result.HistorySince,
                        matches = result.Matches.Select(m => new
                        {
                            drawDate = m.DrawDate,
                            whiteMatches = m.WhiteMatches,
                            specialMatched = m.SpecialMatched,
                            tier = m.TierName,
                            approximateAmount = m.ApproximateAmount,
                            isJackpot = m.IsJackpot,
                        }),
                    }),
                };
            }));

        api.MapGet("/rule-eras", (string game, GetRuleEras useCase) =>
            WithGame(game, g => Results.Ok(useCase.Execute(g))));

        api.MapGet("/generate", (string game, GeneratePicks useCase) =>
            WithGame(game, g =>
            {
                var picks = useCase.Execute(g);
                return Results.Ok(new { game = g.ToString(), whiteBalls = picks.WhiteBalls, special = picks.Special });
            }));
    }

    private static bool TryParseGame(string value, out Game game)
    {
        switch (value.ToLowerInvariant())
        {
            case "powerball":
                game = Game.Powerball;
                return true;
            case "megamillions":
            case "mega-millions":
                game = Game.MegaMillions;
                return true;
            default:
                game = default;
                return false;
        }
    }

    private static IResult WithGame(string raw, Func<Game, IResult> handler) =>
        TryParseGame(raw, out var game)
            ? handler(game)
            : Results.NotFound(new { error = $"Unknown game '{raw}'. Use 'powerball' or 'megamillions'." });

    private static async Task<IResult> WithGameAsync(string raw, Func<Game, Task<IResult>> handler) =>
        TryParseGame(raw, out var game)
            ? await handler(game)
            : Results.NotFound(new { error = $"Unknown game '{raw}'. Use 'powerball' or 'megamillions'." });
}
