using System.Net;

namespace Lottery.Infrastructure.Tests;

/// <summary>
/// Canned HTTP responses for the typed-HttpClient feeds, so every feed test runs
/// fully offline. The handler also records what the feed actually asked for,
/// which is how URL and header construction get pinned.
/// </summary>
internal sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public Uri? LastUri { get; private set; }

    public IReadOnlyList<string> LastAppTokenHeaders { get; private set; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        LastUri = request.RequestUri;
        LastAppTokenHeaders = request.Headers.TryGetValues("X-App-Token", out var values)
            ? values.ToList()
            : [];

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        });
    }
}

/// <summary>
/// A connection factory that reports a dialect nothing supports, for the
/// defensive guards that must fail loudly rather than silently pick a default.
/// It never hands out a connection - reaching <see cref="OpenAsync"/> is itself
/// the failure the test is looking for.
/// </summary>
internal sealed class UnknownDialectConnectionFactory : Lottery.Infrastructure.Persistence.IDbConnectionFactory
{
    public Lottery.Infrastructure.Persistence.SqlDialect Dialect =>
        (Lottery.Infrastructure.Persistence.SqlDialect)999;

    public Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken ct) =>
        throw new NotSupportedException("No connection should be opened for an unknown dialect.");
}
