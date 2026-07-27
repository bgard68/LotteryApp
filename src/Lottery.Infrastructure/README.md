# Lottery.Infrastructure

Adapters: everything that touches a database, a file, or (Phase 2) the
network. Implements the Application layer's ports.

Back to the [main README](../../README.md).

## Persistence (`Persistence/`)

- **No DbContext** - data access is Dapper with hand-written SQL. Row types
  (`DrawRecord`) are DB-shaped and map explicitly to Domain types; dates
  travel as ISO-8601 strings so SQLite and SQL Server map identically.
- **Connection factories** (`IDbConnectionFactory`): connection-per-operation,
  disposed immediately, pooled by the driver. `SqliteConnectionFactory` for
  dev; `SqlServerConnectionFactory` adds `SqlRetryLogicProvider` so the first
  request after an Azure SQL serverless auto-pause retries through the wake-up
  instead of returning a 500.
- **`DrawRepository`**: named queries only. The ticket-match query computes
  white-ball intersection set-wise in pure SQL against the five sorted
  columns. The upsert is dialect-specific (`ON CONFLICT DO NOTHING` /
  `WHERE NOT EXISTS`) and idempotent via the unique `(Game, DrawDate)` index.
- **Migrations** (`Persistence/Migrations/`): plain SQL scripts, embedded in
  the assembly, run by DbUp at startup. Two provider folders (`Sqlite/`,
  `SqlServer/`) share the same numbering and must stay in lockstep.

## Feeds (`Feeds/`)

Live external sources, all behind typed `HttpClient`s with the standard
resilience pipeline (retry + circuit breaker + timeout), registered transient
so handler rotation keeps working:

- **`SocrataWinningNumbersFeed`** (`IWinningNumbersFeed`) - the live NY Open
  Data client; fetches everything after the latest stored draw (incremental
  refresh = gap-repair). Optional `Feeds:SocrataAppToken` raises rate limits.
- **`MegaMillionsJackpotFeed`** - megamillions.com's XML-wrapped JSON service:
  last drawing's jackpot + winner count (rollover) and the next estimate/cash
  value. Parsed defensively; any shape change degrades to null.
- **`NyLotteryJackpotFeed`** - the NY Lottery site API
  (`nylottery.ny.gov/nyl-api`): the primary Powerball jackpot source
  (estimate + cash value, matches powerball.com's display).
- **`PowerballJackpotFeed`** - fallback only: powerball.com retired its
  public JSON API (the route now serves the SPA behind bot protection); this
  adapter returns null unless MUSL restores the endpoint.
- **`CompositeJackpotFeed`** routes each game through its source chain,
  first success wins.

Feed failures are *reported, never thrown* - `RefreshGame` records the error
and the app keeps serving whatever it has. Endpoint URLs, payload shapes, and
quirks: [docs/DATA-SOURCES.md](../../docs/DATA-SOURCES.md).

## Seeding (`Seeding/`)

`SnapshotHistorySource` reads the committed JSON snapshots in `Seeding/Data/`
(embedded resources): 1,971 Powerball draws (2010-2026) and 2,522 Mega
Millions draws (2002-2026), captured from the NY Open Data Socrata datasets
(`d6yy-54nr`, `5xaw-6ayf`). Committed data means first boot is offline and
deterministic, and the era-coverage test can validate the full history on
every CI run. Phase 2 adds the live Socrata client behind the same
`IHistorySource` port for gap-repair and refresh.

## DI (`DependencyInjection.cs`)

`AddInfrastructure(configuration)` is the single registration point:
`Database:Provider` picks SQLite (default) or SqlServer, wires the matching
factory + DbUp, and registers repositories, use cases, `TimeProvider.System`,
and the pick generator.
