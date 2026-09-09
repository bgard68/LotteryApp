using Lottery.Application.UseCases;
using Lottery.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lottery.Api;

public static class LotteryEndpoints
{
    public static void MapLotteryEndpoints(this WebApplication app)
    {
        // Root index: hitting the API's base URL in a browser should explain
        // what lives here rather than returning a bare 404.
        // GET *and* HEAD: the keep-warm monitor and platform probes default to HEAD, and a
        // root endpoint that answers 405 to the probe watching it cannot report bad news.
        app.MapMethods("/", new[] { "GET", "HEAD" }, (IWebHostEnvironment env) => Results.Ok(new
        {
            name = "LotteryApp API",
            games = Enum.GetValues<Game>().Select(g => g.ToString().ToLowerInvariant()),
            docs = env.IsDevelopment() ? "/scalar" : null,
            openApi = "/openapi/v1.json",
            health = "/healthz",
            endpoints = new[]
            {
                "/api/{game}/next-draw",
                "/api/{game}/latest",
                "/api/{game}/draws?from=&to=&limit=",
                "/api/{game}/check?whites=1,2,3,4,5&special=6",
                "/api/{game}/rule-eras",
                "/api/{game}/generate?count=1",
            },
        })).ExcludeFromDescription();

        var api = app.MapGroup("/api/{game}");

        api.MapGet("/next-draw", async (string game, GetNextDraw useCase, CancellationToken ct) =>
            await WithGameAsync(game, async g =>
            {
                var result = await useCase.ExecuteAsync(g, ct);
                return Results.Ok(new
                {
                    game = g.ToString(),
                    drawDate = result.DrawDate,
                    drawTimeUtc = result.DrawTimeUtc,
                    estimatedJackpot = result.EstimatedJackpot,
                    cashValue = result.CashValue,
                    jackpotUpdatedAtUtc = result.JackpotUpdatedAtUtc,
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
                            drawnWhiteBalls = m.DrawnWhiteBalls,
                            drawnSpecial = m.DrawnSpecial,
                            tier = m.TierName,
                            approximateAmount = m.ApproximateAmount,
                            isJackpot = m.IsJackpot,
                        }),
                    }),
                };
            }));

        api.MapGet("/rule-eras", (string game, GetRuleEras useCase) =>
            WithGame(game, g => Results.Ok(useCase.Execute(g))));

        api.MapGet("/generate", (string game, int? count, GeneratePicks useCase) =>
            WithGame(game, g =>
            {
                var requested = count ?? 1;
                if (requested is < GeneratePicks.MinCount or > GeneratePicks.MaxCount)
                    return Results.BadRequest(new
                    {
                        error = $"count must be between {GeneratePicks.MinCount} and {GeneratePicks.MaxCount}.",
                    });

                var picks = useCase.Execute(g, requested);
                return Results.Ok(new
                {
                    game = g.ToString(),
                    tickets = picks.Tickets.Select(t => new { whiteBalls = t.WhiteBalls, special = t.Special }),
                });
            }));

        // Refresh trigger for the keep-alive workflow (and manual catch-up).
        // Optionally guarded by a shared key: set Refresh:Key in the environment
        // (never in a committed file) and callers send X-Refresh-Key.
        app.MapPost("/internal/refresh", async (HttpRequest request, RefreshGame refresh,
            IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var requiredKey = configuration["Refresh:Key"];
            if (!string.IsNullOrEmpty(requiredKey)
                && request.Headers["X-Refresh-Key"].ToString() != requiredKey)
            {
                return Results.Unauthorized();
            }

            var logger = loggerFactory.CreateLogger("Lottery.Api.Refresh");
            var results = new List<RefreshResult>();

            foreach (var game in Enum.GetValues<Game>())
            {
                try
                {
                    results.Add(await refresh.ExecuteAsync(game, ct));
                }
                // Per game rather than around the loop: one game's source
                // failing must not cost the other game its refresh, which is
                // what an unguarded loop did.
                //
                // RefreshGame reports the failure modes it anticipates. This
                // catches the ones it does not, so a new escape degrades to a
                // reported error instead of a 500 - and this is the endpoint
                // the keep-alive workflow calls, so a 500 here reads as the
                // whole instance being down.
                //
                // OperationCanceledException is deliberately excluded: a
                // shutdown or a disconnected client is not a feed failure and
                // must keep propagating.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Game}: refresh failed, reporting it as a feed error.", game);
                    results.Add(new RefreshResult(game, UpToDate: false, NewDraws: 0,
                        SkippedInvalid: 0, JackpotUpdated: false, FeedError: ex.Message));
                }
            }

            return Results.Ok(results.Select(r => new
            {
                game = r.Game.ToString(),
                upToDate = r.UpToDate,
                newDraws = r.NewDraws,
                skippedInvalid = r.SkippedInvalid,
                jackpotUpdated = r.JackpotUpdated,
                feedError = r.FeedError,
            }));
        });
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
