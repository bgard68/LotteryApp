using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// megamillions.com utility service: XML-wrapped JSON carrying the last drawing,
/// current/next jackpot pools, cash values, and jackpot winner count (winner
/// count zero = rollover). Undocumented endpoint - parsed defensively; any
/// shape change degrades to null rather than throwing.
/// </summary>
public sealed class MegaMillionsJackpotFeed(HttpClient http) : IJackpotFeed
{
    private const string Endpoint = "https://www.megamillions.com/cmspages/utilservice.asmx/GetLatestDrawData";

    public async Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        if (game != Game.MegaMillions)
            return null;

        MmPayload? payload;
        try
        {
            var xml = await http.GetStringAsync(Endpoint, ct);
            var json = XDocument.Parse(xml).Root?.Value;
            if (string.IsNullOrWhiteSpace(json))
                return null;

            payload = JsonSerializer.Deserialize<MmPayload>(json, JsonOptions);
        }
        // Both sibling feeds already guard exactly this, and the summary above
        // promises it. Without it a bot-challenge HTML page served with a 200 -
        // which is precisely what retired powerball.com's API - reaches
        // XDocument.Parse and throws a type RefreshGame's catch filter does not
        // match, so /internal/refresh returns 500 instead of a feed error.
        catch (Exception ex) when (ex is HttpRequestException or XmlException or JsonException)
        {
            return null;
        }

        if (payload?.Jackpot is null)
            return null;

        DateOnly? lastDrawDate = DateTime.TryParse(payload.Jackpot.PlayDate, out var d)
            ? DateOnly.FromDateTime(d)
            : null;

        return new JackpotInfo(
            Game.MegaMillions,
            lastDrawDate,
            payload.Jackpot.CurrentPrizePool,
            payload.Jackpot.JackpotWinners is { } winners ? winners > 0 : null,
            payload.Jackpot.NextPrizePool,
            payload.Jackpot.NextCashValue);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record MmPayload(MmJackpot? Jackpot);

    private sealed record MmJackpot(
        string? PlayDate,
        decimal? CurrentPrizePool,
        int? JackpotWinners,
        decimal? NextPrizePool,
        decimal? NextCashValue);
}
