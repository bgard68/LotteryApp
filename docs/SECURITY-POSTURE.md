# Security posture

What is enabled on this repository, **why**, and the audit that produced the
current configuration. For reporting a vulnerability see
[SECURITY.md](../SECURITY.md); for the architectural decisions behind the
zero-secrets design see
[requirements and decisions](REQUIREMENTS-AND-DECISIONS.md) (D14).

Back to the [main README](../README.md).

## The design does most of the work

Before any tooling, the architecture removes whole categories of risk:

- **No secrets exist to leak.** Local dev is SQLite (a file path, not a
  credential); production uses Azure Managed Identity, so no SQL password
  exists anywhere; deploys use GitHub OIDC federation, so no deployment
  credential is stored. The single optional secret - a Socrata rate-limit
  token - lives in `dotnet user-secrets`, outside the repo tree.
- **No user data.** The database holds public lottery results and nothing
  else: no accounts, no PII, no payment data.
- **No authentication surface**, because there is nothing to protect. The one
  state-changing endpoint (`POST /internal/refresh`) takes an optional shared
  header key supplied from the environment.
- **Read-only public API** behind a per-IP rate limiter.

That is the context for everything below: the tooling defends the supply chain
and the code, because there is no data or credential to defend.

## Enabled controls

| Control | Status | Why |
|---|---|---|
| Secret scanning | On | Catches a committed credential even though the design should never produce one |
| Push protection | On | Blocks the push *before* a secret enters history - remediation after the fact is far worse |
| Non-provider secret patterns | Unavailable | Requires GitHub Advanced Security, which this free public repo does not have; the API accepts the setting and silently leaves it disabled |
| Dependabot alerts + security updates | On | Vulnerable dependency opens a fix PR without waiting for the weekly schedule |
| Dependabot version updates | Weekly, grouped | NuGet + npm + GitHub Actions across both branches (see below) |
| CodeQL | Push, PR, weekly | C# on `main`, JavaScript/TypeScript on `frontend`, `security-extended` query suite |
| Private vulnerability reporting | On | SECURITY.md directs reporters to it, so it has to actually work |
| Branch protection | Both branches | Requires a PR and green CI; blocks force-pushes and deletions |
| CI as a gate | Every push and PR | Build, 68 backend tests, and a live smoke test against a real running instance |

## Audit findings and fixes

An audit of the security tooling (2026-07-28) found zero open alerts of any
kind - and four configuration gaps that meant some of that clean result was
simply *unexamined* code. Each is recorded here with the fix.

### F1 - The entire frontend had no security coverage

**Why it happened:** CodeQL was configured for `csharp` and triggered on
`main` only. The Angular app lives on `frontend`, which `main` never
contains - so it was never scanned, and no JavaScript/TypeScript analysis
existed at all. Separately, Dependabot only reads its config from the
**default branch** and had no `npm` ecosystem, so `lottery-web`'s package
tree - by far the largest dependency surface in the project - was
unmonitored. This is the direct cost of the never-merge branch policy: nothing
propagates between branches by itself.

**Fix:** an `npm` Dependabot entry with `directory: /lottery-web` and
`target-branch: frontend` (plus a second `github-actions` entry for that
branch's workflows), and a CodeQL workflow committed to `frontend` analyzing
JavaScript/TypeScript. Angular packages are grouped so they upgrade in
lockstep rather than arriving as a dozen individually-broken PRs.

### F2 - CodeQL was running in reduced-fidelity mode

**Why it happened:** the workflow used `build-mode: none`, chosen originally
for speed. Buildless C# analysis does not fully resolve dependencies, so
dataflow *through* libraries is exactly where it is weakest - which for this
codebase means the Dapper SQL paths and the feed HTTP clients, the two places
worth analyzing most.

**Fix:** switched to `build-mode: manual` with an explicit `dotnet build`, and
added the `security-extended` query suite. Costs roughly two minutes per run.

### F3 - SECURITY.md promised a feature that was disabled

**Why it happened:** the policy was written pointing at GitHub's private
advisory form, but private vulnerability reporting had never been switched on
in repository settings - so the documented reporting path did not work.

**Fix:** enabled the setting. (The general lesson: a security policy that
references a feature is a claim, and claims need verifying.)

### F4 - Neither branch was protected

**Why it happened:** the repository was created and pushed to directly; branch
protection was on the Phase 4 list but never applied. Both `main` and
`frontend` could be force-pushed or deleted, and nothing required the CI that
had just been built to pass.

**Fix:** protection on both branches - pull request required, CI required
green, force-pushes and deletions blocked. Administrator enforcement is
deliberately **off**: this is a single-maintainer repository, and locking the
only maintainer out of an emergency fix is a worse failure mode than the
bypass it prevents. Protection here is a guard against accident, not against a
malicious owner.

## Dependency update policy

Dependabot opens PRs; merging them is a judgment call, not a formality:

1. **Never merge on stale checks.** PRs opened before a CI workflow existed
   carry only the checks that existed then. Rebase (`@dependabot rebase`) so
   the current gates actually run before trusting a green tick - and remember
   that a passing CodeQL run in buildless mode does not even compile the code.
2. **Grouped minor/patch updates** merge on green CI.
3. **Major versions get verified against the code that uses them**, not just a
   build. `Microsoft.Data.SqlClient` majors, for example, touch
   `SqlConfigurableRetryFactory`/`SqlRetryLogicOption` - the Azure serverless
   wake-up path that no local test exercises, since repository tests run on
   SQLite.
4. **Deliberate pins stay pinned, with the reason recorded on the PR.**
   `Microsoft.OpenApi` is held at 2.11.0: that pin exists to patch a known
   vulnerability, and `Microsoft.AspNetCore.OpenApi` 10.0.0 targets the 2.x
   line. Taking 3.x risks breaking the OpenAPI document, and with it the
   generated TypeScript client and its drift check.

## What is deliberately not done

- **A gitleaks CI step.** Secret scanning with push protection covers the same
  ground earlier (at push time rather than after the commit lands).
- **SHA-pinning actions.** Tag pinning plus Dependabot keeps actions current;
  SHA pinning trades that automation for supply-chain rigidity that this
  repository's threat model does not justify.
- **A CSPRNG for pick generation.** Reviewed and rejected with reasons - see
  [decision D21](REQUIREMENTS-AND-DECISIONS.md#external-review-analyzed-why-not-a-csprng-d21).
