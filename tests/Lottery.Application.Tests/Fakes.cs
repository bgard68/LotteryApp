using Lottery.Application.Abstractions;
using Lottery.Domain;

namespace Lottery.Application.Tests;

public sealed class FakeDrawRepository : IDrawRepository
{
    public List<Draw> Draws { get; } = [];

    public Task<Draw?> GetLatestAsync(Game game, CancellationToken ct) =>
        Task.FromResult(Draws.Where(d => d.Game == game).OrderByDescending(d => d.DrawDate).FirstOrDefault());

    public Task<IReadOnlyList<Draw>> GetRangeAsync(Game game, DateOnly? from, DateOnly? to, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Draw>>(Draws
            .Where(d => d.Game == game && (from is null || d.DrawDate >= from) && (to is null || d.DrawDate <= to))
            .OrderByDescending(d => d.DrawDate)
            .Take(limit)
            .ToList());

    public Task<int> CountAsync(Game game, CancellationToken ct) =>
        Task.FromResult(Draws.Count(d => d.Game == game));

    public Task<DateOnly?> EarliestDrawDateAsync(Game game, CancellationToken ct) =>
        Task.FromResult(Draws.Where(d => d.Game == game).Select(d => (DateOnly?)d.DrawDate).Min());

    public Task<IReadOnlyList<MatchRow>> FindMatchesAsync(Game game, IReadOnlyList<int> whites, int special, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MatchRow>>(Draws
            .Where(d => d.Game == game)
            .Select(d => TicketMatcher.Match(d, whites, special))
            .Where(m => m.WhiteMatches > 0 || m.SpecialMatched)
            .Select(m => new MatchRow(m.DrawDate, m.WhiteMatches, m.SpecialMatched))
            .ToList());

    public Task<bool> UpsertAsync(Draw draw, CancellationToken ct)
    {
        if (Draws.Any(d => d.Game == draw.Game && d.DrawDate == draw.DrawDate))
            return Task.FromResult(false);
        Draws.Add(draw);
        return Task.FromResult(true);
    }

    public Task BulkInsertAsync(IReadOnlyList<Draw> draws, CancellationToken ct)
    {
        Draws.AddRange(draws);
        return Task.CompletedTask;
    }

    public Task UpdateJackpotAsync(Game game, DateOnly drawDate, decimal? jackpotAmount, bool? jackpotWon, CancellationToken ct)
    {
        var index = Draws.FindIndex(d => d.Game == game && d.DrawDate == drawDate);
        if (index >= 0)
            Draws[index] = Draws[index] with { JackpotAmount = jackpotAmount, JackpotWon = jackpotWon };
        return Task.CompletedTask;
    }
}

public sealed class FakeJackpotStore : Lottery.Application.Abstractions.IJackpotStore
{
    public Lottery.Application.Abstractions.JackpotEstimate? Saved { get; private set; }

    public Task<Lottery.Application.Abstractions.JackpotEstimate?> GetAsync(Game game, CancellationToken ct) =>
        Task.FromResult(Saved?.Game == game ? Saved : null);

    public Task SaveAsync(Lottery.Application.Abstractions.JackpotEstimate estimate, CancellationToken ct)
    {
        Saved = estimate;
        return Task.CompletedTask;
    }
}

public sealed class FakeNumbersFeed(IReadOnlyList<Draw> draws) : Lottery.Application.Abstractions.IWinningNumbersFeed
{
    public Exception? ThrowOnFetch { get; set; }
    public DateOnly? LastRequestedAfter { get; private set; }

    public Task<IReadOnlyList<Draw>> GetDrawsAfterAsync(Game game, DateOnly after, CancellationToken ct)
    {
        if (ThrowOnFetch is not null) throw ThrowOnFetch;
        LastRequestedAfter = after;
        return Task.FromResult<IReadOnlyList<Draw>>(
            draws.Where(d => d.Game == game && d.DrawDate > after).OrderBy(d => d.DrawDate).ToList());
    }
}

public sealed class FakeJackpotFeed(Lottery.Application.Abstractions.JackpotInfo? info) : Lottery.Application.Abstractions.IJackpotFeed
{
    public Task<Lottery.Application.Abstractions.JackpotInfo?> GetJackpotAsync(Game game, CancellationToken ct) =>
        Task.FromResult(info?.Game == game ? info : null);
}

public sealed class FakeImportLedger : IImportLedger
{
    private readonly Dictionary<Game, ImportRecord> _records = [];

    public Task<ImportRecord?> GetAsync(Game game, CancellationToken ct) =>
        Task.FromResult(_records.TryGetValue(game, out var r) ? r : null);

    public Task RecordAsync(ImportRecord record, CancellationToken ct)
    {
        _records[record.Game] = record;
        return Task.CompletedTask;
    }
}

public sealed class FakeHistorySource(IReadOnlyList<Draw> draws) : IHistorySource
{
    public int Calls { get; private set; }
    public string Name => "fake";

    public Task<IReadOnlyList<Draw>> GetHistoryAsync(Game game, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<Draw>>(draws.Where(d => d.Game == game).ToList());
    }
}
