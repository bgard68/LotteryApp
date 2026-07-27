using System.Text.Json;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// Best-effort Powerball jackpot source. powerball.com retired its public JSON
/// API (the old /api/v1/estimates route now serves the SPA behind bot
/// protection), so this adapter attempts the endpoint and returns null on
/// anything that is not clean JSON - the app then shows Powerball numbers and
/// countdowns without dollar amounts, per the graceful-degradation design.
/// If MUSL restores a structured endpoint, only this class changes.
/// </summary>
public sealed class PowerballJackpotFeed(HttpClient http) : IJackpotFeed
{
    private const string Endpoint = "https://www.powerball.com/api/v1/estimates/powerball?_format=json";

    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        if (game != Game.Powerball)
            return null;

        string body;
        try
        {
            body = await http.GetStringAsync(Endpoint, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith('[') && !trimmed.StartsWith('{'))
            return null; // HTML or bot-challenge page, not data

        try
        {
            var rows = JsonSerializer.Deserialize<List<PbEstimate>>(trimmed, JsonOptions);
            var row = rows?.FirstOrDefault();
            if (row is null)
                return null;

            return new JackpotInfo(
                Game.Powerball,
                LastDrawDate: null,
                LastJackpot: null,
                LastJackpotWon: null,
                NextEstimatedJackpot: ParseMoney(row.field_prize_amount),
                NextCashValue: ParseMoney(row.field_prize_amount_cash));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"$412 Million" / "$277.3 Million" / "$950,000" -> decimal dollars.</summary>
    internal static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = text.Replace("$", "").Replace(",", "").Trim();
        var multiplier = 1m;

        if (cleaned.EndsWith(" Billion", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000_000m;
            cleaned = cleaned[..^8];
        }
        else if (cleaned.EndsWith(" Million", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000m;
            cleaned = cleaned[..^8];
        }

        return decimal.TryParse(cleaned, out var value) ? value * multiplier : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed class PbEstimate
    {
        public string? field_next_draw_date { get; set; }
        public string? field_prize_amount { get; set; }
        public string? field_prize_amount_cash { get; set; }
    }
}
