# Tests

281 tests across four projects, one per layer. Run with `dotnet test`.

The PowerShell smoke test in [scripts](../scripts/README.md) adds **32 checks**
against a real running API - every endpoint, the error conditions, the refresh
guard in both directions, and five security-header assertions (one of which
asserts the *absence* of the `Server` header).

Back to the [main README](../../README.md).

## Lottery.Domain.Tests (86)

Pure logic, no mocks needed:

- **`DrawScheduleTests`** - next/previous draw math, including the 10:59 PM
  rollover minute and a **DST-end boundary** case (the Saturday draw before
  and Monday draw after the November clock change land at different UTC
  offsets).
- **`DrawScheduleBoundaryTests`** - the *other* boundaries: the exact draw
  instant (next-draw is strictly-after, previous-draw is at-or-before), the
  **spring DST transition**, winter/summer `DrawInstantUtc` differing by
  exactly the DST hour, and Mega Millions' schedule math.
- **`RuleEraTests`** - era boundaries on their exact dates (e.g. Powerball
  2015-10-06 vs 10-07), plus the planted-violation test proving
  `EraValidator` catches out-of-era numbers.
- **`EraValidatorTests`** - single-draw validation: the era consulted is the
  draw *date's* era, not today's (both directions), violation messages name
  the balls and bounds, and `RuleEra.IsValidDraw` enforces every rule (count,
  distinctness, both range ends for whites and special).
- **`TicketMatcherTests`** - order independence, and the two classic lottery
  bugs asserted impossible: a white ball never matches the special ball and
  vice versa.
- **`DrawTests`** - `Draw.Create` invariants (exactly 5 distinct whites,
  stored sorted, jackpot facts carried) and the record's content-based value
  equality that repository round-trip tests depend on.
- **`GameExtensionsTests`** - the draw calendar (Mon/Wed/Sat vs Tue/Fri),
  draw times (22:59 vs 23:00 Eastern), and special-ball names as reference
  facts.
- **`PrizeTiersTests`** - every tier's exact name, amount, and jackpot flag
  for both games (18 rows), non-winning combinations mapping to no tier, and
  `DisplayAmount`'s three branches.
- **`PickGeneratorTests`** - seeded determinism, era validity over hundreds
  of draws, and full-range coverage (every ball 1..69 and 1..26 appears -
  guards off-by-one at both ends).

## Lottery.Application.Tests (70)

Use cases against in-memory fakes + `FakeTimeProvider` (virtual time - the
Pending test advances the clock past a drawing in microseconds):

- **`GetLatestDrawTests`** - Published vs **Pending** when the schedule says a
  drawing happened but no numbers are stored.
- **`CheckTicketTests`** - every rejection reason, `DataUnavailable` vs
  zero-matches, tier mapping of wins, exclusion of non-winning partials.
- **`CheckTicketResultShapeTests`** - the successful-check contract: matches
  ordered newest-first, `DrawsChecked` counts the whole history (not just
  hits), `HistorySince` is the oldest stored draw, the jackpot row carries no
  fixed amount, game isolation, and Mega Millions bounds using the current
  70/24 matrix and the "Mega Ball" name.
- **`ImportHistoryTests`** - runs once then skips forever; an era violation
  aborts with nothing written; an empty source throws.
- **`RefreshGameTests`** - gap-repair fetches from the latest stored draw;
  up-to-date games skip the feed; feed failures are reported not thrown;
  era-invalid feed rows are skipped; jackpot info saves the estimate and
  stamps the stored draw.
- **`RefreshGameJackpotEdgeTests`** - the partial-payload matrix: a throwing
  jackpot feed stays *silent* (numbers still stored, no `FeedError`),
  estimate-only info saves without touching draws, last-draw-only info stamps
  without saving, all-null info persists nothing, an empty database
  gap-repairs from the beginning of time, and a feed timeout surfaces as
  `FeedError`.

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

## Lottery.Infrastructure.Tests (61)

Against a **real SQLite database** (temp file, migrated by DbUp per test class)
- with Dapper the SQL is the logic, so mocking the connection would test
nothing:

- **`SqliteRepositoryTests`** - round-trips, idempotent upsert, game
  isolation, set-wise SQL matching, range/limit queries.
- **`DrawRepositoryEdgeTests`** - the SQL paths the base suite leaves open:
  the `MIN()` earliest-date aggregate (and its null on empty), the jackpot
  `UPDATE` (including a no-op on an unknown date), the null-bound halves of
  the range query, and the match query's inclusion rule - special-alone rows
  come back with zero whites, two-whites-no-special rows are filtered out
  server-side, and games never cross.
- **`ImportLedgerTests`** - the seed-once guard's round-trip fidelity,
  including the recorded timestamp's *offset*, null for an unrecorded game,
  and per-game isolation.
- **`DatabaseInitializerTests`** - running migrations twice is a safe no-op
  that preserves data (every boot re-runs them).
- **`SnapshotHistoryTests`** - the committed snapshots load, contain no
  duplicates, are ordered - and the **era-coverage test**: all 4,493 real
  draws must fit the known rule eras, so an undocumented lottery rule change
  (or bad feed data) fails the suite loudly. This same suite is what the
  planned weekly CI run executes to catch future rule changes.
- **`FeedParsingTests`** - contract tests against recorded real payloads: the
  megamillions.com XML-wrapped JSON parses (including rollover from winner
  count), powerball.com's HTML response degrades to null instead of throwing,
  and money strings ("$1.5 Billion") parse correctly.
- **`MegaMillionsFeedEdgeTests`** - winner-count semantics (positive means
  won), the wrong-game short-circuit that never even calls the endpoint, and
  degrade-to-null for empty, partial, non-XML, and malformed-JSON payloads
  (the latter two are regression tests for an `XmlException` that escaped
  the jackpot-is-optional design).
- **`SocrataFeedTests`** - both directions of the live gap-repair contract:
  the outgoing request (per-game dataset id, strictly-after `$where`, limit,
  the optional `X-App-Token` header present *and* absent) and the row
  mapping (Powerball's six-number string vs Mega Millions' separate
  `mega_ball` field, whites normalized to sorted order, a missing mega ball
  throws).
- **`CompositeJackpotFeedTests`** - source routing: Mega Millions uses only
  megamillions.com, Powerball tries the NY Lottery API first and only falls
  back to powerball.com when NY is unusable, unused sources are never called,
  and every-source-dead degrades to null.
- **`JackpotStoreTests`** - estimate round-trip and upsert on real SQLite.
- **`SqliteDirectoryTests`** - regression coverage for the production crash
  loop: connection strings pointing into directories that do not exist yet.

## Lottery.Api.Tests (64)

**In-process API tests** over the real stack - `WebApplicationFactory` boots
the actual `Program` (DbUp migrations, the committed snapshot seed, Dapper
over a temp SQLite file, the rate limiter, every endpoint). Only the two live
feeds and the clock are replaced: no test may touch the network, and a
`FakeTimeProvider` pins "now" to Monday 2026-07-27 noon Eastern, which makes
both games' snapshot tails the current Published draws and every next-draw
instant exactly computable.

- **`RootAndHealthTests`** - the root index payload (games, endpoints, docs
  link), `/healthz` reporting Healthy against the seeded database, the
  OpenAPI document, the exact unknown-game 404 across all six endpoints,
  case-insensitive routing plus the `mega-millions` alias, and GET-only
  enforcement (405).
- **`NextDrawAndLatestTests`** - exact UTC draw instants from the pinned
  clock (Powerball Tue 02:59, Mega Millions Wed 03:00), and the full
  jackpot pipeline: stub feed -> refresh cycle -> SQLite -> response
  (estimates on `next-draw`, amount/won stamps on `latest`).
- **`CheckEndpointTests`** - the snapshot's own final Powerball draw checked
  as a ticket reports the jackpot hit (committed data, not luck), whites
  order-independent over HTTP, and the exact 400 for every rejection: missing
  params, the named non-numeric offender, count/distinct/range violations,
  and Mega Millions bounds under its current era.
- **`DrawsAndGenerateTests`** - default page of 50 newest-first with exact
  first rows, inclusive date ranges, the 200 cap on `?limit=1000000`, zero
  clamping up to one row, malformed query values rejected by binding, and
  generated tickets valid for each game's current matrix with count bounds
  enforced (exact 400 message).
- **`RuleErasEndpointTests`** - seven Powerball eras / five Mega Millions
  eras over HTTP, ascending, exactly one current, with the exact boundary
  dates and matrices.
- **`SecurityBehaviorTests`** - the four baseline security headers on success
  *and* error responses, Development serving Scalar without CSP, Production
  locking down (exact CSP value, HSTS, no docs link, `/scalar` 404, OpenAPI
  still public), and config-driven CORS in both directions.
- **`RateLimitTests`** - a tiny-permit host: bursts beyond the permit get
  429 while early requests succeed, attacker-prepended `X-Forwarded-For`
  entries cannot split the partition (`ForwardLimit = 1` honors only the
  platform-appended rightmost entry), and distinct real clients keep
  independent budgets.
- **`RefreshEndpointTests`** - an unguarded refresh runs a full cycle per
  game and reports it field-by-field; configuring `Refresh:Key` enforces the
  header guard in both directions.
- **`DrawRefreshServiceTests`** - the background loop on virtual time: one
  gap-repair cycle at startup (without fetching current games), waking five
  minutes after the next drawing to store the published result, polling on
  the ten-minute backoff until the feed publishes and then going quiet, and
  clean cancellation.
