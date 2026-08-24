using System.Text.Json;
using Lottery.Application.Abstractions;
using Lottery.Domain;
using Microsoft.Extensions.Configuration;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// Live NY Open Data (Socrata) client - the same datasets the committed
/// snapshots were captured from, used for incremental refresh and gap-repair.
/// The optional app token only raises rate limits; the API works without it.
/// </summary>
public sealed class SocrataWinningNumbersFeed : IWinningNumbersFeed
{
    private const string PowerballDataset = "d6yy-54nr";
    private const string MegaMillionsDataset = "5xaw-6ayf";

    private readonly HttpClient _http;

    public SocrataWinningNumbersFeed(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://data.ny.gov/");
        var token = configuration["Feeds:SocrataAppToken"];
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Add("X-App-Token", token);
    }

    public async Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct)
    {
        var dataset = game == Game.Powerball ? PowerballDataset : MegaMillionsDataset;
        var where = Uri.EscapeDataString($"draw_date > '{after:yyyy-MM-dd}T23:59:59'");
        var url = $"resource/{dataset}.json?$where={where}&$order=draw_date&$limit=200";

        try
        {
            using var stream = await _http.GetStreamAsync(url, ct);
            var rows = await JsonSerializer.DeserializeAsync<List<SocrataRow>>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Socrata feed returned null.");

            return rows.Select(r => ToDraw(game, r)).ToList();
        }
        // A malformed payload, or a row whose winning_numbers is short or
        // non-numeric, otherwise escapes as JsonException / FormatException /
        // ArgumentOutOfRangeException - none of which RefreshGame's catch
        // filter matches, so one bad row 500s /internal/refresh rather than
        // being reported as a feed error. Rethrown as the type the caller
        // does handle, keeping the cause: the batch is still refused rather
        // than silently delivered short.
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(
                $"Socrata feed returned an unusable payload: {ex.Message}", ex);
        }
    }

    private static Draw ToDraw(Game game, SocrataRow row)
    {
        var numbers = row.winning_numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

        // Powerball rows carry 6 numbers (last = special); Mega Millions rows
        // carry 5 whites with the Mega Ball in its own field.
        var (whites, special) = game == Game.Powerball
            ? (numbers[..5], numbers[5])
            : (numbers[..5], int.Parse(row.mega_ball
                ?? throw new InvalidOperationException("Mega Millions row missing mega_ball.")));

        return Draw.Create(game, DateOnly.Parse(row.draw_date.AsSpan(0, 10)), whites, special);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SocrataRow
    {
        public string draw_date { get; set; } = "";
        public string winning_numbers { get; set; } = "";
        public string? mega_ball { get; set; }
    }
}
