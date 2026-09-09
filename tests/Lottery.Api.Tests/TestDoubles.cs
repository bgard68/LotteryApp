using System.Text.Json;
using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Api.Tests;

/// <summary>
/// Live-feed stand-ins for the test host: no test may ever reach the network.
/// Counters are thread-safe because the background refresh service calls these
/// from pool threads while tests observe them.
/// </summary>
public sealed class StubWinningNumbersFeed : IWinningNumbersFeed
{
    private readonly List<Draw> _published = [];
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Simulates the lottery publishing a result to the live feed.</summary>
    public void Publish(Draw draw)
    {
        lock (_published) _published.Add(draw);
    }

    public Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (_published)
        {
            return Task.FromResult<IReadOnlyList<Draw>>(_published
                .Where(d => d.Game == game && d.DrawDate > after)
                .OrderBy(d => d.DrawDate)
                .ToList());
        }
    }
}

public sealed class StubJackpotFeed(params JackpotInfo[] infos) : IJackpotFeed
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(infos.FirstOrDefault(i => i.Game == game));
    }
}

/// <summary>In-memory IDrawRepository for driving DrawRefreshService on virtual time.</summary>
public sealed class InMemoryDrawRepository : IDrawRepository
{
    private readonly List<Draw> _draws = [];
    private readonly object _gate = new();

    public void Seed(params Draw[] draws)
    {
        lock (_gate) _draws.AddRange(draws);
    }

    public bool Contains(Game game, DateOnly drawDate)
    {
        lock (_gate) return _draws.Any(d => d.Game == game && d.DrawDate == drawDate);
    }

    public Task<Draw?> GetLatestAsync(Game game, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_draws
                .Where(d => d.Game == game)
                .OrderByDescending(d => d.DrawDate)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Draw>>(_draws
                .Where(d => d.Game == game && (from is null || d.DrawDate >= from) && (to is null || d.DrawDate <= to))
                .OrderByDescending(d => d.DrawDate)
                .Take(limit)
                .ToList());
        }
    }

    public Task<int> CountAsync(Game game, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_draws.Count(d => d.Game == game));
    }

    public Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_draws
                .Where(d => d.Game == game)
                .Select(d => (DateOnly?)d.DrawDate)
                .Min());
        }
    }

    public Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MatchRow>>(_draws
                .Where(d => d.Game == game)
                .Select(d => (Draw: d, Match: TicketMatcher.Match(d, whites, special)))
                .Where(x => x.Match.WhiteMatches > 0 || x.Match.SpecialMatched)
                .Select(x => new MatchRow(x.Match.DrawDate, x.Draw.WhiteBalls, x.Draw.Special,
                    x.Match.WhiteMatches, x.Match.SpecialMatched))
                .ToList());
        }
    }

    public Task<bool> UpsertAsync(Draw draw, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_draws.Any(d => d.Game == draw.Game && d.DrawDate == draw.DrawDate))
                return Task.FromResult(false);
            _draws.Add(draw);
            return Task.FromResult(true);
        }
    }

    public Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct)
    {
        lock (_gate) _draws.AddRange(draws);
        return Task.CompletedTask;
    }

    public Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct)
    {
        lock (_gate)
        {
            var index = _draws.FindIndex(d => d.Game == game && d.DrawDate == drawDate);
            if (index >= 0)
                _draws[index] = _draws[index] with { JackpotAmount = jackpotAmount, JackpotWon = jackpotWon };
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryJackpotStore : IJackpotStore
{
    private readonly Dictionary<Game, JackpotEstimate> _estimates = [];
    private readonly object _gate = new();

    public Task<JackpotEstimate?> GetAsync(Game game, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_estimates.TryGetValue(game, out var e) ? e : null);
    }

    public Task SaveAsync(JackpotEstimate estimate, CancellationToken ct)
    {
        lock (_gate) _estimates[estimate.Game] = estimate;
        return Task.CompletedTask;
    }
}

/// <summary>JSON plumbing for endpoint assertions.</summary>
internal static class ApiJson
{
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static int[] IntArray(JsonElement element) =>
        element.EnumerateArray().Select(e => e.GetInt32()).ToArray();

    /// <summary>For arrays whose elements the contract guarantees non-null.</summary>
    public static string[] StringArray(JsonElement element) =>
        element.EnumerateArray().Select(e => e.GetString()!).ToArray();
}

/// <summary>Real-time settling for work the fake clock has already released.</summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(condition(), $"Timed out waiting for: {because}");
    }
}
