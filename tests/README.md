# Tests

66 tests across three projects, one per layer. Run with `dotnet test`.

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

## Lottery.Application.Tests (19)

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

## Lottery.Infrastructure.Tests (23)

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
