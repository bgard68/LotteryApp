# Lessons learned

Real problems encountered while building this project, with root causes and
fixes - kept so the next phase (and the next project) doesn't repeat them.

Back to the [main README](../README.md).

## 1. `dotnet new` templates clash with Central Package Management (NU1008)

**Symptom:** `dotnet new xunit` failed its post-create restore with
`error NU1008: PackageReference items cannot define a value for Version`.

**Cause:** the repo root `Directory.Packages.props` enables Central Package
Management for every project underneath, but the templates generate csproj
files with inline `Version="..."` attributes - CPM forbids inline versions.

**Fix:** rewrite the generated csprojs with version-less `PackageReference`
entries (versions live only in `Directory.Packages.props`).
**Lesson:** when CPM is on, expect every `dotnet new` template's restore to
fail until its csproj is cleaned; scaffold the csproj by hand or clean it
immediately after.

## 2. `TreatWarningsAsErrors` surfaced real dependency problems on first build

**Symptom:** first solution build failed with NU1601 (dbup-sqlserver resolved
to 6.0.16, not the pinned 6.0.4) and two NU1903 **known-vulnerability** errors
(`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 via Microsoft.Data.Sqlite,
`Microsoft.OpenApi` 2.0.0 via Microsoft.AspNetCore.OpenApi).

**Fix:** pin `dbup-sqlserver` to the transitively-required 6.0.16, and add
direct references lifting the vulnerable transitive packages to patched
versions (`SQLitePCLRaw.bundle_e_sqlite3` 3.0.4, `Microsoft.OpenApi` 2.11.0),
verified against the NuGet flat-container API rather than guessing versions.
**Lesson:** warnings-as-errors plus NuGet audit turns "ship with a known CVE"
into a build break on day one - keep it on, and lift vulnerable transitives
with explicit top-level pins.

## 3. C# records compare collections by reference

**Symptom:** two repository round-trip tests failed: a `Draw` read back from
SQLite was "not equal" to the one written, despite identical values.

**Cause:** `Draw` is a record with an `IReadOnlyList<int> WhiteBalls` property.
Record value-equality compares that property with `EqualityComparer<T>.Default`
- reference equality for collections - so a rehydrated list never matches.

**Fix:** custom `Equals`/`GetHashCode` on the record using
`SequenceEqual` for the white balls. Fixed in the domain type, not the tests -
draws being value-equal is real domain behavior.
**Lesson:** records + collection properties silently break value semantics;
override equality (or use immutable value collections) whenever a record
carries a list.

## 4. Windows PowerShell 5.1 hides HTTP error bodies

**Symptom:** the smoke test's error-condition checks failed even though the
API returned the correct 400/404 status codes - the assertions on the response
body matched nothing.

**Cause:** in Windows PowerShell 5.1, `Invoke-WebRequest` throws on non-2xx
and the error body is not reliably on `$_.ErrorDetails.Message`; it must be
read from `$_.Exception.Response.GetResponseStream()`.

**Fix:** the smoke test's catch block now tries `ErrorDetails` first
(PowerShell 7+) and falls back to reading the response stream (5.1).
**Lesson:** scripts intended to run in CI (pwsh 7) and locally (5.1) need the
dual path, or a `#requires` pin to one shell.

## 5. `git add --renormalize` does not stage new files

**Symptom:** while rebuilding history, the Phase 1 code commit came up empty -
`git add -A --renormalize` left every new file untracked.

**Cause:** `--renormalize` implies update-only semantics: it re-checks
line-ending normalization for **already-tracked** files and ignores untracked
ones, even when combined with `-A`.

**Fix:** `git add -A` (stage everything) as a separate step; renormalize is
only for re-applying changed `.gitattributes` to an existing tree.
**Lesson:** `--renormalize` is not "add plus normalize" - it changes the
meaning of the whole command.

## 6. The era table had to be proven against real data, not written from memory

**Symptom / risk:** the rule-era table (which number ranges were valid in which
years) was drafted from documented rule-change dates. Any wrong boundary would
either fail the import or, worse, validate bad data.

**Fix:** the era-coverage test runs `EraValidator` over all 4,493 committed
historical draws - every draw must fit its era. Getting this green required
seven Powerball eras (back to 1992) and five Mega Millions eras, including the
easy-to-miss 2012 Powerball special-ball reduction (39 -> 35) and the April
2025 Mega Millions revamp (25 -> 24).
**Lesson:** reference data transcribed from documentation is a hypothesis
until validated against the full real dataset; commit the dataset and make the
validation a permanent test. (A weekly scheduled CI run of the same suite will
catch *future* rule changes within days.)

## 7. GitHub template `.gitignore` files are not a safe default

**Observation from repo setup:** the stock Visual Studio `.gitignore` template
is ~350 lines of mostly-dead rules, and the common `.vscode/` +
`!.vscode/settings.json` pattern found in the wild **silently does nothing** -
negations cannot re-include files under an ignored *directory*; the working
idiom is `.vscode/*` (ignore contents) plus negations.
**Lesson:** curate the ignore file to the actual stack, comment the why, and
use the `dir/*` + `!dir/file` idiom when re-including; verify with
`git check-ignore -v`.

## 8. The undocumented-endpoint risk materialized - before a line of feed code was written (Phase 2)

**Symptom:** the powerball.com JSON API the design named as the jackpot source
(`/api/v1/estimates/powerball?_format=json`) no longer exists: it now returns
the SPA homepage, the site is server-rendered (no XHR data calls to borrow),
and it sits behind bot protection that empty-handed both PowerShell and curl.

**Fix:** the design's own fallback: draw dates come from schedule math (never
needed a feed), Powerball jackpot amounts are null end-to-end and hidden in
the UI, and a best-effort adapter (with a contract test for the old payload
shape) stays in place should MUSL restore an endpoint. Mega Millions' service
works fully and supplies estimate, cash value, and rollover status.
**Lesson:** treat every undocumented endpoint as already dead when designing -
"nullable + hide gracefully + fallback for the critical part" meant this
discovery cost an hour, not a redesign. Probe sources *before* building
against them.

**Postscript:** a replacement source surfaced by probing sibling government
sites: the NY Lottery's own site API
(`nylottery.ny.gov/nyl-api/games/powerball/draws`) serves the estimated
jackpot and cash value as clean JSON, matching powerball.com's display
exactly. It is now the primary Powerball jackpot source, with the retired
MUSL adapter demoted to fallback. Second lesson: when an official API dies,
check the *state lottery sites* that display the same numbers - one of them
is usually serving JSON to its own frontend without bot protection.

## 9. Windows PowerShell can't decompress modern web responses

**Symptom:** probing endpoints with `Invoke-WebRequest`/`Invoke-RestMethod`
returned binary garbage (Brotli-compressed bodies PS 5.1 cannot decode), and
`curl.exe --compressed` returned empty (bot-protected TLS). The endpoint's
true status was only visible in a real browser.

**Lesson:** when probing an API, garbage or empty output is not proof the
endpoint is dead - check compression (`Accept-Encoding`) and bot protection
before concluding; a real browser is the ground truth.

## 10. xUnit InlineData cannot widen int to long?

**Symptom:** `[InlineData("$633 Million", 633_000_000)]` into a
`long? expected` parameter failed at runtime with "Object of type
'System.Int32' cannot be converted to type 'System.Nullable`1[System.Int64]'" -
xUnit stores InlineData values as boxed objects and does not perform numeric
widening into nullable parameters.

**Fix:** explicit `L` suffixes (`633_000_000L`).
**Lesson:** InlineData literals must match the parameter type exactly when the
parameter is nullable; the C# compiler's implicit conversions don't apply to
boxed theory data.

## 11. Typed HttpClients must not be captured by singletons

**Near-miss during DI wiring:** the first draft registered feed adapters
(typed `HttpClient` classes) as singletons. A singleton captures one
`HttpClient` forever, which defeats `IHttpClientFactory`'s handler rotation
and eventually causes stale-DNS failures in long-running services.

**Fix:** feeds and the `RefreshGame` use case that consumes them are
transient; the hosted service resolves them through a scope per refresh cycle.
**Lesson:** anything registered via `AddHttpClient<T>` is transient by
contract - every consumer up the chain must be transient/scoped, or resolve
through `IServiceScopeFactory`.

## 12. GitHub `.github/README.md` shadows the root README

**Near-miss while writing docs:** GitHub's README display precedence is
`.github/` > repo root > `docs/`. Dropping a "workflows readme" at
`.github/README.md` would have replaced the main project README on the repo
home page. The workflows doc lives at `.github/workflows/README.md` instead.
**Lesson:** never place a README directly in `.github/` unless you intend it
to be the repo's displayed README.

## 13. Vite's dev proxy reports a dead backend as 500-with-empty-body (Phase 3)

**Symptom (user-reported):** clicking "Generate picks" in the Angular app
showed a generic "Something went wrong - try again". The API endpoint itself
was fine - the .NET backend simply wasn't running while the frontend dev
server was.

**Cause, two layers deep:** (1) local dev is a two-process setup (Angular dev
server + API) and nothing surfaced that the second process was down; (2) when
its proxy target refuses connections, Angular's Vite-based dev server responds
**HTTP 500 with an empty body** - not the 502/504 a classic gateway returns -
so a naive "is this a gateway error?" status check misclassifies it as a real
server error, and the store's generic catch hid the root cause.

**Fix:** the HTTP adapter classifies status 0, 502, 503, 504, *and*
500-with-empty-body (a real API 500 always carries a problem-details body)
into a typed `ApiUnreachableError`; the stores map it to "Can't reach the
lottery API. If you're running locally, start the backend first." Spec-covered,
and verified live in both states. Troubleshooting documented in
`lottery-web/README.md`.
**Lesson:** never collapse transport failures and server failures into one
generic catch - classify at the adapter (the only layer that knows HTTP), and
learn your dev proxy's actual failure signature by observing it, not assuming
gateway conventions.

## 14. PowerShell 5.1 mangles embedded double quotes in native command arguments

**Symptom:** `git commit -m @'...'@` with a here-string containing a quoted
phrase ("Something went wrong") failed bizarrely - git saw the message split
into multiple pathspec arguments.

**Cause:** Windows PowerShell 5.1's native-argument encoding re-quotes
arguments containing embedded double quotes incorrectly, splitting the
here-string at the quotes.

**Fix:** write the message to a temp file and use `git commit -F <file>` -
immune to every quoting rule.
**Lesson:** for multi-line native-command input containing quotes in PS 5.1,
pass a file, not an argument.

## 15. The desktop grid was one pixel-rule away from overflowing every phone

**Symptom:** none reported - found while adding the mobile layout. The app had
**zero** `@media` queries; on a 320px-wide phone the page scrolled sideways and
the ticket-entry row ran off the screen.

**Cause:** two fixed sizes that only ever met a wide window.
`grid-template-columns: repeat(auto-fit, minmax(320px, 1fr))` sets a *minimum*
of 320px, so on a 320px viewport (minus 32px of body padding) the grid item was
wider than its container and pushed the document out. Separately, the ticket
row's six `width: 3.1rem` inputs plus gaps needed ~300px before the checkbox
and label were counted.

`auto-fit` is widely treated as "responsive by default". It is not: it collapses
*empty* tracks, but a non-empty track never shrinks below the `minmax` minimum.
Every layout with a hard minimum has a viewport at which it overflows.

**Fix:** `minmax(0, 1fr)` below the breakpoint, and inputs switched to
`flex: 1 1 0; min-width: 0` so six share whatever width exists. Verified by
asserting `document.documentElement.scrollWidth === window.innerWidth` at both
375px and 320px, on every tab, with 10 tickets and 100 match rows rendered.

**Lesson:** a fixed minimum is a promise about the viewport. Grep for `minmax(`,
`min-width`, and fixed `width` on anything that must fit a phone, and test the
overflow assertion rather than eyeballing a screenshot - horizontal overflow of
a few pixels is invisible in a screenshot and obvious to a thumb.

### Postscript: the emulated viewport lies about resize events

Verifying the desktop-to-phone transition through Chrome DevTools Protocol
looked like a bug in the app: `matchMedia('(max-width: 640px)').matches` flipped
correctly, the CSS media query applied, but the Angular layout stayed on the old
one. A probe settled it - CDP's device-metrics override changes the viewport
**without dispatching `resize` or `MediaQueryList` `change`**, so no listener
fires. Real browsers and real phone rotations do fire both.

**Lesson:** when emulated-viewport behaviour disagrees with the code, prove
which one is wrong before "fixing" anything - arm a counter on the event you
depend on. The transition is covered by a spec driving `FakeViewport` instead,
which is where that assertion belonged anyway.
