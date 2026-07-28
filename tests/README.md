# Tests

109 tests across three projects, one per layer. Run with `dotnet test`.

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
