# Lessons learned

Every defect and near-miss encountered while building this project: **what
caught it**, **why it happened**, **the fix applied**, and the generalizable
lesson - kept so the next phase (and the next project) doesn't repeat them.
A summary of which detector caught what is at the [end](#what-caught-what).

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

## 15. The rate limit was tighter than normal use

**Symptom (user-reported):** clicking "Generate picks" showed a generic
"Something went wrong - try again" - while previously-loaded results still sat
on screen, making it look self-contradictory. The API log showed
`429 Too Many Requests`.

**Cause:** the per-IP ceiling was 60 requests/minute. A 5-ticket check is 5
API calls and a page load is ~5 more, so an actively clicking user crossed it
in ordinary use. The frontend then lumped 429 into its generic catch, hiding
which limit had been hit.

**Fix:** default raised to 120/minute and made configurable
(`RateLimit:PermitPerMinute`); the adapter now throws a typed
`RateLimitedError` mapped to "Checking a little too fast - wait a few seconds
and try again."
**Lesson:** size a rate limit against the app's own busiest legitimate
interaction (here: one click = N calls), not against a round number - and give
every distinct failure mode its own message, or the limit becomes
indistinguishable from a bug.

## 16. History rows all rendered the same numbers

**Symptom (user-reported):** every row of the check-history list showed an
identical set of six numbers, making the results look broken.

**Cause:** a display error - the match rows rendered *the user's ticket* with
matching balls flashing, so all 72 rows repeated the same numbers. The backend
was already returning each drawing's numbers
(`drawnWhiteBalls`/`drawnSpecial`); the template simply bound the wrong source.

**Fix:** rows now render the drawing's winning numbers with the balls that
appear on the user's ticket flashing, and the hint text states that convention
explicitly.
**Lesson:** when a list renders the same data in every row, that is the bug -
and it is invisible to unit tests, which is exactly why UI changes get looked
at in a browser here rather than declared done.

## 17. A threshold change flooded the "big wins" panel

**Symptom (user-reported):** the big-win callout filled with a dozen $7
prizes, losing all signal.

**Cause:** I interpreted "3 or more numbers plus the Powerball" as *3 total
matched numbers counting the special ball*, which admits `Match 2 + Powerball`.
Across 1,971 drawings nearly every ticket has several of those.

**Fix:** threshold restored to 3+ matching **white** balls AND the special
ball ($100 tier and up), with the rule spelled out in the panel title.
**Lesson:** when a requirement counts things, confirm *which* things before
implementing - and sanity-check a threshold against the data volume it will
face, because "rare" over 1,971 drawings is a very different bar than "rare"
over one.

## 18. The committed API client drifted from the backend contract

**Symptom:** the new OpenAPI drift check failed on its very first CI run.

**Cause:** the multi-ticket feature added a `count` query parameter to
`/api/{game}/generate`, but the committed `schema.d.ts` was never regenerated -
exactly the drift the check exists to prevent, already present before the check
was written.

**Fix:** regenerated from the live document and committed; the check now
guards every push.
**Lesson:** generated artifacts committed to a repo *will* go stale silently -
a regenerate-and-diff CI step is the only thing that makes
"generated from the contract" true rather than aspirational. Adding one to an
existing repo usually finds drift immediately.

## 19. The dev server served a stale bundle after a branch switch

**Symptom:** browser verification showed old UI (once, an empty page) while
tests passed and the code on disk was correct. Happened twice.

**Cause:** `lottery-web/` exists only on the `frontend` branch. Switching to
`main` while `ng serve` was running deleted the files under its watcher; the
rebuild failed (`Cannot find tsconfig file`) and it kept serving the last good
bundle.

**Fix:** restart `ng serve` after any branch switch; stop dev servers before
switching deliberately.
**Lesson:** file-watching dev servers assume a stable working tree - branch
switches violate that assumption silently, and the symptom (stale UI) looks
like a code bug rather than a tooling one.

## What caught what

| Detector | Entries |
|---|---|
| Compiler / build gates (warnings-as-errors, NuGet audit, CPM) | 1, 2 |
| Automated tests | 3, 6, 10 |
| CI checks (OpenAPI drift) | 18 |
| Live smoke test | 4 |
| Pre-implementation probing | 8, 9 |
| Self-review during implementation | 7, 11, 12 |
| Browser verification | 19 |
| Tooling errors surfaced immediately | 5, 14 |
| User report | 13, 15, 16, 17 |

Fifteen of nineteen were caught by automation or deliberate verification before
a user saw them - the argument for keeping the gates strict, since
warnings-as-errors, the real-instance smoke test, and the drift check each
paid for themselves within days of existing. The four user-reported defects
share a trait: **all were presentation or threshold decisions, not logic
errors.** The domain code was correct every time; the mistake was in what got
displayed (16), what got hidden (13), where a boundary was drawn (17), or how
generous a limit was (15). That is precisely the class of defect unit tests
cannot catch.

## 20. Dependabot PRs failed locked-mode restore

**Symptom:** CI failed with `NU1004` on the first Dependabot NuGet PR to run
under the new workflow: the lock file said 6.1.1, central package management
said 6.1.6, and `dotnet restore --locked-mode` refused the mismatch.

**Cause:** this repo commits `packages.lock.json` and restores locked (a
deliberate reproducibility gate). Dependabot updates
`Directory.Packages.props` but does not regenerate the lock files - so every
NuGet update PR arrives self-inconsistent. The two features are individually
correct and jointly broken.

**Fix:** regenerate on the PR branch (`dotnet restore --force-evaluate`),
verify build + tests, and push the lock files to the same PR. Recorded in the
dependency-update policy so future updates do this as a matter of course.
**Lesson:** every gate you add changes what "a passing dependency update"
requires - when you turn on locked-mode restore, automated update PRs inherit
a new manual step until tooling catches up.

## 21. Security configuration drifted silently as the project changed shape

**Symptom:** an on-demand audit (prompted by the user, not by any process)
found four gaps at once - the entire frontend had no CodeQL or Dependabot
coverage, CodeQL ran in reduced-fidelity buildless mode, SECURITY.md pointed
at a reporting feature that was never enabled, and neither branch was
protected - plus seven dependency PRs sitting untriaged. Every finding is
recorded individually in SECURITY-POSTURE.md (F1-F4); this entry is the root
cause they share.

**Cause:** security settings were configured once, at repo creation, when the
project was C#-only with a single branch - and were correct for that shape.
The architecture then changed underneath them: the frontend branch appeared
with a new language and package ecosystem, the never-merge policy made it a
separate world, and CI arrived expecting protection that was still "planned".
Nothing forced a re-check because **configuration does not fail loudly** -
wrong code breaks a test, but a branch CodeQL silently does not cover stays
green forever. Each phase rigorously verified its own new code while the
cross-cutting settings quietly went stale. Contributing failure: noticing
without scheduling (the stale Dependabot PRs were spotted early and left for
"later") is the same as not noticing.

**Fix:** the audit itself, plus SECURITY-POSTURE.md as the living record of
what is enabled and why - a written claim that can be re-verified, unlike a
memory of having clicked a setting once.
**Second instance, found later:** the documented `dotnet user-secrets` path
could not run at all, because no `UserSecretsId` was ever added to the project
(audit finding F5) - an instruction written but never executed, exactly like
the disabled reporting feature in F3.

**Lesson:** audit settings at every phase boundary, not once at setup. Any
change to the repo's *shape* - a new branch, language, package ecosystem, or
workflow - should trigger the question "does the security and CI
configuration still match?" Config needs the same verification cadence as
code, precisely because it has no tests to fail on its behalf. And every
documented security procedure should be **run once** before it is published -
an instruction nobody has executed is a guess.

## 22. .NET 9+ removed Swagger UI, so a fresh API has no docs page

**Symptom:** running the API from Visual Studio opens the browser at
`https://localhost:7012/` and shows **HTTP 404**. The expectation - formed by
a decade of ASP.NET Core templates - is a Swagger page.

**Cause:** two separate things, both correct behaviour:

1. **The API has no route at `/`.** It serves `/api/{game}/...`, `/healthz`
   and `/openapi/v1.json`; nothing was mapped to the root, and
   `launchSettings.json` had `launchBrowser: true` with no `launchUrl`, so the
   browser landed exactly where nothing exists.
2. **There is no Swagger UI to land on.** .NET 9 removed Swashbuckle from the
   web templates. The built-in replacement, `AddOpenApi()` / `MapOpenApi()`,
   generates the OpenAPI *document* only - it ships no user interface at all.
   This project chose the built-in generator deliberately (decision D1), which
   also means it inherited the missing UI.

**Fix:** map a root index that describes the API (games, endpoints, links to
the document and health check) so the base URL is informative rather than a
404, and add **Scalar** (`Scalar.AspNetCore`) for an interactive reference at
`/scalar` in Development only - the document stays public, but a browsable UI
is not something a production API needs to expose. `launchUrl` points there so
Visual Studio opens something useful.
**Lesson:** when a framework removes a batteries-included default, the gap is
silent - nothing errors, you just get a 404 where a page used to be. Verify
what the *base URL* of a service actually returns; it is the first thing any
human hitting the service will see, and it is easy to never test because no
automated check ever browses it.

## 23. A RID-specific publish silently rewrites the lock files

**Symptom:** trimming the deployment payload with
`dotnet publish -r linux-x64` worked locally, but CI then failed every
project with `NU1004: The project's runtime identifiers have changed`.

**Cause:** the publish performed an implicit restore *for that RID*, which
rewrote the committed `packages.lock.json` files to include linux-x64 assets.
Those rewritten files were committed along with the workflow change. CI's
restore runs **without** a RID and in `--locked-mode`, so the lock files it
found no longer matched the projects it was restoring - the mirror image of
the Dependabot lock-file mismatch in the dependency policy.

**Fix:** declare the target explicitly -
`<RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>` in
`Directory.Build.props` - so the RID is part of the normal restore graph.
Both paths then agree: a plain locked-mode restore and a
`publish -r linux-x64` produce the same lock content.
**Lesson:** committed lock files make the *restore graph* part of your source,
so any flag that changes that graph - a RID, a target framework, a package
addition - has to be declared in the project rather than passed ad hoc on one
command. Otherwise the flag silently edits a committed file and the next
locked restore rejects it.

## 24. The provisioning script had never been run

**Symptom:** the first real run of `provision-azure.ps1` died immediately at
the resource-group existence check, and after that fix died again on
`az webapp create`.

**Cause, both times:** Windows PowerShell 5.1 wraps every stderr line from a
native command in an ErrorRecord, and `$ErrorActionPreference = 'Stop'` makes
those terminating. `az ... show` writes to stderr when a resource is absent -
which is the *normal answer* for an existence check, not a failure - and
`az webapp create` emits a harmless deprecation WARNING on stderr. Neither is
an error; both killed the script.

**Fix:** the az helpers drop to `'Continue'` for the duration of the call,
judge success by `$LASTEXITCODE` alone, and filter ErrorRecords out before
parsing JSON. An `Invoke-AzOptional` helper covers calls that legitimately
fail when the resource already exists.

**The real lesson is not about PowerShell.** This is the same failure as F3
(a security policy pointing at a disabled feature) and F5 (a documented
user-secrets command that could not run): **a script written, documented in
detail, and never executed.** It was committed with confident prose about what
it would do, and the first two things it actually did were crash. Having
already written that lesson down twice did not prevent a third instance -
which suggests the rule needs to be mechanical rather than remembered:
anything executable ships only after it has been run, or it ships explicitly
labelled as unverified.

## 25. A missing directory became a crash loop that burned a day's CPU quota

**Symptom:** the first deployment to Azure succeeded, then every endpoint
returned **503**. The App Service reported `state: QuotaExceeded` and Azure
served its own "Web App - Unavailable" page. An entire day's F1 allowance -
60 CPU-minutes - was gone within roughly fifteen minutes of the app existing.

**Cause:** the provisioning script set the connection string to
`Data Source=/home/data/lottery.db`, and **nothing created `/home/data`**.
SQLite creates database *files*; it never creates *directories*. On a fresh
App Service that path does not exist, so startup threw
"unable to open database file".

That alone would be a small bug. What made it expensive was the **feedback
loop**: startup work ran before `app.Run()`, so the exception killed the
process; App Service restarted it; it failed again. Metrics recorded
**51 worker stop requests**. Each attempt re-ran migrations and re-attempted
the 4,493-row seed from scratch, because the import ledger is only written on
success. Fifty-one failed seedings is what consumed the quota - not the cost
of seeding once, which is trivial.

**Why nothing caught it:** every repository test used a path under the system
temp directory, which always exists. The one condition that mattered in
production - a directory that is absent - was the one condition never
exercised. The local run had the same blind spot: `lottery.db` sat beside the
binary in a directory that obviously existed.

**Fixes applied, in order of value:**

1. **The bug.** `SqliteConnectionFactory` now creates the parent directory at
   construction, before anything connects. Handles `:memory:` and
   directory-less relative paths.
2. **The regression test.** `SqliteDirectoryTests` opens a database in a
   directory that does not exist, and runs migrations against a nested missing
   path. Verified honestly: with the fix disabled, 2 of 4 tests fail.
3. **The loop.** Startup database work is wrapped in a `try`/`catch` that logs
   `LogCritical` **including the connection string in use** and lets the app
   start anyway. It fails loudly and *once* instead of restarting forever.
4. **The signal.** `/healthz` now runs a real `DatabaseHealthCheck` - a cheap
   count query proving connectivity, schema, and seeded data. A process that
   is running but cannot read its data now reports **Unhealthy** instead of
   answering "OK" while every real request 500s.

**Being proactive about this class of failure:**

- **Never let startup work kill the process on a platform that restarts you.**
  App Service, Kubernetes and systemd all restart on exit, which converts one
  configuration error into an unbounded resource burn. Catch, log, stay up,
  report unhealthy - a dead-but-diagnosable instance beats an invisible loop.
- **Health checks must ask the dependency, not the process.** "The web server
  answered" proves nothing about whether the app can work.
- **Test the absence, not just the presence.** Missing directory, missing file,
  missing environment variable, empty database. Tests that only run in
  well-formed environments certify nothing about a fresh one.
- **Read the platform's own counters before theorising.** `WPStopRequests: 51`
  identified a restart loop in seconds and disproved my first explanation
  (that seeding over network storage was inherently expensive). Quota state,
  restart counts and usage metrics are all one CLI call away.
- **Environment parity is where these hide.** The bug needed three things
  present at once - a path with a directory component, that directory being
  absent, and a platform that restarts on exit. No local run had all three.
  When production differs structurally from dev, enumerate the differences and
  test each one deliberately.
