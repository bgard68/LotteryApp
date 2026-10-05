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
        // A malformed payload, or a row whose winning_numbers is short,
        // non-numeric, or not five distinct balls, otherwise escapes as
        // JsonException / FormatException / ArgumentException - none of which
        // RefreshGame's catch filter matches, so one bad row 500s
        // /internal/refresh rather than being reported as a feed error.
        //
        // ArgumentException rather than ArgumentOutOfRangeException: Draw.Create
        // throws the BASE type for a row that is not five distinct balls, which
        // is what the feed publishes in the minutes after a drawing. Catching
        // only the derived type let exactly that case through.
        //
        // Rethrown as the type the caller does handle, keeping the cause: the
        // batch is still refused rather than silently delivered short.
        // IndexOutOfRangeException is in the list because an array index is not
        // an ArgumentException: a Powerball row carrying five numbers instead of
        // six passed the `[..5]` slice and then threw on `[5]`, escaping both
        // this filter and RefreshGame's. ToDraw now rejects that row by its
        // length before indexing, so this arm is the backstop rather than the
        // guard - kept so a future edit that reintroduces a bare index degrades
        // to a reported feed error instead of breaking the promise above.
        catch (Exception ex) when (ex is JsonException or FormatException
            or ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException(
                $"Socrata feed returned an unusable payload: {ex.Message}", ex);
        }
    }

    /// <summary>How many numbers <c>winning_numbers</c> carries, per dataset.</summary>
    private static int ExpectedNumbers(Game game) => game == Game.Powerball ? 6 : 5;

    private static Draw ToDraw(Game game, SocrataRow row)
    {
        var numbers = row.winning_numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

        // Checked before anything indexes it, and this is the point: the feed
        // publishes partial rows in the minutes after a drawing, and a Powerball
        // row short of its sixth number used to reach `numbers[5]` and throw
        // IndexOutOfRangeException - which is not an ArgumentException, so it
        // escaped the conversion below that exists for exactly this case. It
        // then escaped RefreshGame's filter too, aborting the refresh before
        // the jackpot step rather than being reported as a feed error.
        //
        // A length check converts the whole class at its source. The type of
        // the exception stops being load-bearing.
        if (numbers.Length != ExpectedNumbers(game))
            throw new InvalidOperationException(
                $"{game} row for {row.draw_date} carries {numbers.Length} number(s), expected {ExpectedNumbers(game)}.");

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
