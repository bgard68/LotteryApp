# Tests

175 tests across four projects, one per layer. Run with `dotnet test`.

The PowerShell smoke test in [scripts](../scripts/README.md) adds **32 checks**
against a real running API - every endpoint, the error conditions, the refresh
guard in both directions, and five security-header assertions (one of which
asserts the *absence* of the `Server` header).

Back to the [main README](../../README.md).

## Lottery.Domain.Tests (24)

Pure logic, no mocks needed:

- **`DrawScheduleTests`** - next/previous draw math, including the 10:59 PM
  rollover minute and a **DST-end boundary** case (the Saturday draw before
  and Monday draw after the November clock change land at different UTC
  offsets).
- **`RuleEraTests`** - era boundaries on their exact dates (e.g. Powerball
  2015-10-06 vs 10-07), plus the planted-violation test proving
  `EraValidator` catches out-of-era numbers.
- **`TicketMatcherTests`** - order independence, and the two classic lottery
  bugs asserted impossible: a white ball never matches the special ball and
  vice versa.
- **`PickGeneratorTests`** - seeded determinism, era validity over hundreds
  of draws, and full-range coverage (every ball 1..69 and 1..26 appears -
  guards off-by-one at both ends).

## Lottery.Application.Tests (56)

Use cases against in-memory fakes + `FakeTimeProvider` (virtual time - the
Pending test advances the clock past a drawing in microseconds):

- **`GetLatestDrawTests`** - Published vs **Pending** when the schedule says a
  drawing happened but no numbers are stored.
- **`CheckTicketTests`** - every rejection reason, `DataUnavailable` vs
  zero-matches, tier mapping of wins, exclusion of non-winning partials.
- **`ImportHistoryTests`** - runs once then skips forever; an era violation
  aborts with nothing written; an empty source throws.
- **`RefreshGameTests`** - gap-repair fetches from the latest stored draw;
  up-to-date games skip the feed; feed failures are reported not thrown;
  era-invalid feed rows are skipped; jackpot info saves the estimate and
  stamps the stored draw.

### Use-case bounds (added by the 2026-07-28 audit)

The four use cases that had no test file now have one. All four guard a
*boundary*, which is exactly what was going unasserted:

- **`GetDrawsTests`** - the `limit` clamp, the only user-supplied number that
  reaches a query: null defaults to 50, 0 and negatives clamp to 1, and
  anything past `MaxLimit` (200) - including `int.MaxValue` - clamps down, so
  `?limit=1000000` can never become a table scan. Plus inclusive date ranges
  and game isolation.
- **`GeneratePicksTests`** - count bounds 1/10 accepted, 0/-1/11/`int.MaxValue`/
  `int.MinValue` rejected. These were previously asserted only over HTTP by the
  smoke test; they are a use-case rule, and the endpoint is one caller rather
  than the only possible one. Also: every generated ticket is era-valid with
  distinct whites, and a batch of ten is not ten copies of one ticket.
- **`GetNextDrawTests`** - a missing jackpot estimate nulls the amounts without
  costing the countdown (the graceful-degradation promise, asserted); one
  game never borrows another's estimate; and a sweep of every hour in a week
  proves the next draw is *always* in the future, including the minutes right
  after a drawing.
- **`GetRuleErasTests`** - exactly one era is current (zero would disable
  frontend validation, two would make it ambiguous), the boundary day of an
  era already uses the new rules, and a clock set before any era began still
  yields one current era instead of throwing.

## Lottery.Infrastructure.Tests (29)

Against a **real SQLite database** (temp file, migrated by DbUp per test class)
- with Dapper the SQL is the logic, so mocking the connection would test
nothing:

- **`SqliteRepositoryTests`** - round-trips, idempotent upsert, game
  isolation, set-wise SQL matching, range/limit queries.
- **`SnapshotHistoryTests`** - the committed snapshots load, contain no
  duplicates, are ordered - and the **era-coverage test**: all 4,493 real
  draws must fit the known rule eras, so an undocumented lottery rule change
  (or bad feed data) fails the suite loudly. This same suite is what the
  planned weekly CI run executes to catch future rule changes.
- **`FeedParsingTests`** - contract tests against recorded real payloads: the
  megamillions.com XML-wrapped JSON parses (including rollover from winner
  count), powerball.com's HTML response degrades to null instead of throwing,
  and money strings ("$1.5 Billion") parse correctly.
- **`JackpotStoreTests`** - estimate round-trip and upsert on real SQLite.

## Lottery.Api.Tests (66)

The API layer had no tests at all: `Program.cs`, `LotteryEndpoints.cs`,
`DrawRefreshService.cs` and `DatabaseHealthCheck.cs` were covered only by the
smoke test against a running process, which runs after a build rather than as
part of one.

`LotteryApiFactory` boots the **real** host over a throwaway SQLite file - real
migrations, real snapshot seed, real routing and middleware. Only the live
feeds and the timer-driven refresh loop are replaced, so a run needs no network
and no clock:

- **`EndpointTests`** - the game-name vocabulary (`powerball`,
  `megamillions`, `mega-millions`, case-insensitively) and a 404 for anything
  else on *every* endpoint, covering both the sync and async guard paths.
  Every error branch each handler can take: missing query parameters, a
  non-numeric white ball named back to the caller, an out-of-era ticket,
  `count` outside 1..10 at both ends. Also the contract the SPA renders from -
  `latest` reports **Pending** with null numbers rather than presenting a
  stale draw as current.
- **`SecurityHeaderTests` / `DevelopmentPipelineTests`** - the four hardening
  headers on success *and* on the responses that never reach an endpoint body
  (the game guard's 404, a validation 400, a routing 404), the absent `Server`
  header, and the CSP asserted in both directions: sent in Production, and
  deliberately absent in Development where Scalar is a real HTML page.
- **`RefreshEndpointTests`** - `/internal/refresh` is the one endpoint that
  writes and the only one with an access check. Both configurations are
  pinned: open when no `Refresh:Key` is set (deliberate, for a single-host
  deployment), and 401 without the header, with a wrong key, or with the right
  key in the wrong case.
- **`DatabaseHealthCheckTests`** - all three verdicts, including the one that
  matters most: a reachable but **empty** database is Degraded, not Healthy,
  because that is the signature of a half-finished startup.
- **`DrawRefreshServiceTests`** - the background loop on `FakeTimeProvider`.
  Startup gap-repair covers both games; one game's feed failing does not cost
  the other its refresh; a shutdown mid-wait completes rather than faults; and
  the loop wakes and refreshes again once the next drawing has passed.

## Coverage

`dotnet test --settings coverlet.runsettings` writes Cobertura reports. Those
settings exclude the OpenAPI source generator's output - several thousand
generated lines in `Lottery.Api` that nothing we write can cover, and which
alone cost that layer about forty points - so the number answers "how much of
*our* code is exercised".
