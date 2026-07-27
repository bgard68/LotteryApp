using System.Text.Json;
using System.Text.Json.Serialization;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// NY Lottery's site API (nylottery.ny.gov/nyl-api) - a government-run,
/// structured JSON source whose upcoming-draw entry carries the estimated
/// jackpot and cash value. Primary Powerball jackpot source now that MUSL has
/// retired powerball.com's public API; verified to match powerball.com's
/// displayed figures. Undocumented, so parsed defensively - any shape change
/// degrades to null.
/// </summary>
public sealed class NyLotteryJackpotFeed(HttpClient http) : IJackpotFeed
{
    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        var slug = game == Game.Powerball ? "powerball" : "megamillions";

        string body;
        try
        {
            body = await http.GetStringAsync($"https://nylottery.ny.gov/nyl-api/games/{slug}/draws", ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        NyResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NyResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        // The upcoming draw is the entry that carries jackpot figures.
        var upcoming = payload?.Data?.Draws?
            .Where(d => d.EstimatedJackpot is not null || d.Jackpots is { Count: > 0 })
            .OrderByDescending(d => d.DrawTime)
            .FirstOrDefault();

        if (upcoming is null)
            return null;

        var jackpot = upcoming.Jackpots?.FirstOrDefault();
        return new JackpotInfo(
            game,
            LastDrawDate: null,
            LastJackpot: null,
            LastJackpotWon: null,
            NextEstimatedJackpot: upcoming.EstimatedJackpot ?? jackpot?.Amount,
            NextCashValue: jackpot?.CashAmount);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed class NyResponse
    {
        public NyData? Data { get; set; }
    }

    internal sealed class NyData
    {
        public List<NyDraw>? Draws { get; set; }
    }

    internal sealed class NyDraw
    {
        public long DrawTime { get; set; }
        public decimal? EstimatedJackpot { get; set; }
        public List<NyJackpot>? Jackpots { get; set; }
    }

    internal sealed class NyJackpot
    {
        public decimal? Amount { get; set; }
        [JsonPropertyName("cashAmount")]
        public decimal? CashAmount { get; set; }
    }
}
