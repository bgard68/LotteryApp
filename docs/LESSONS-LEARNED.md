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

## 8. GitHub `.github/README.md` shadows the root README

**Near-miss while writing docs:** GitHub's README display precedence is
`.github/` > repo root > `docs/`. Dropping a "workflows readme" at
`.github/README.md` would have replaced the main project README on the repo
home page. The workflows doc lives at `.github/workflows/README.md` instead.
**Lesson:** never place a README directly in `.github/` unless you intend it
to be the repo's displayed README.
