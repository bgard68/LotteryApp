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
/// <remarks>
/// Rows this adapter cannot make sense of are reported as
/// <see cref="WinningNumbersResult.Unavailable"/>, not thrown and not silently dropped: the feed
/// publishes partial rows in the minutes after a drawing, and a batch that is short a number is
/// refused whole rather than delivered incomplete. Anything this method fails to anticipate is
/// turned into the same outcome by <see cref="FaultTolerantWinningNumbersFeed"/>, so no payload
/// can escape as an exception regardless of what it provokes here.
/// </remarks>
public sealed class SocrataWinningNumbersFeed : IWinningNumbersFeed
{
    private readonly HttpClient _http;

    public SocrataWinningNumbersFeed(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://data.ny.gov/");
        var token = configuration["Feeds:SocrataAppToken"];
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Add("X-App-Token", token);
    }

    public async Task<WinningNumbersResult> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct)
    {
        var profile = GameFeedProfiles.For(game);
        var where = Uri.EscapeDataString($"draw_date > '{after:yyyy-MM-dd}T23:59:59'");
        var url = $"resource/{profile.SocrataDataset}.json?$where={where}&$order=draw_date&$limit=200";

        using var stream = await _http.GetStreamAsync(url, ct);
        var rows = await JsonSerializer.DeserializeAsync<List<SocrataRow>>(stream, JsonOptions, ct);

        if (rows is null)
            return WinningNumbersResult.Unavailable("Socrata feed returned null.");

        var draws = new List<Draw>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryReadDraw(game, profile, row, out var draw, out var reason))
                return WinningNumbersResult.Unavailable($"Socrata feed returned an unusable payload: {reason}");

            draws.Add(draw);
        }

        return WinningNumbersResult.Ok(draws);
    }

    /// <summary>
    /// Converts one row, or explains why it cannot be converted. Every field is checked before it
    /// is used, so a malformed row produces a reason rather than an exception whose type the
    /// caller would have to recognise.
    /// </summary>
    private static bool TryReadDraw(
        Game game, GameFeedProfile profile, SocrataRow row, out Draw draw, out string reason)
    {
        draw = null!;

        var tokens = row.winning_numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i], out numbers[i]))
            {
                reason = $"{game} row for {row.draw_date} carries non-numeric ball '{tokens[i]}'.";
                return false;
            }
        }

        // Checked before anything indexes it: the feed publishes partial rows in the minutes after
        // a drawing, and a Powerball row short of its sixth number must be refused by its length
        // rather than by whatever indexing it would throw.
        if (numbers.Length != profile.WinningNumbersCount)
        {
            reason = $"{game} row for {row.draw_date} carries {numbers.Length} number(s), "
                   + $"expected {profile.WinningNumbersCount}.";
            return false;
        }

        int special;
        if (profile.SpecialIsLastWinningNumber)
        {
            special = numbers[^1];
        }
        else if (!int.TryParse(row.mega_ball, out special))
        {
            reason = $"{game} row for {row.draw_date} is missing or has a non-numeric special ball.";
            return false;
        }

        var whites = numbers[..RuleEra.WhiteBallCount];

        if (row.draw_date.Length < 10 || !DateOnly.TryParse(row.draw_date.AsSpan(0, 10), out var drawDate))
        {
            reason = $"{game} row has an unreadable draw_date '{row.draw_date}'.";
            return false;
        }

        // The era table is the authority on what is a valid draw; ask it before Draw.Create, which
        // signals the same refusal by throwing. A date no era covers is a feed problem too - it
        // used to reach RuleEras.ForDate and escape as ArgumentOutOfRangeException.
        if (!RuleEras.TryForDate(game, drawDate, out var era))
        {
            reason = $"no known {game} rule era covers {drawDate:yyyy-MM-dd}.";
            return false;
        }

        if (!era.IsValidDraw(whites, special))
        {
            reason = $"{game} row for {drawDate:yyyy-MM-dd} is not five distinct balls valid for its era.";
            return false;
        }

        draw = Draw.Create(game, drawDate, whites, special);
        reason = "";
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SocrataRow
    {
        public string draw_date { get; set; } = "";
        public string winning_numbers { get; set; } = "";
        public string? mega_ball { get; set; }
    }
}
