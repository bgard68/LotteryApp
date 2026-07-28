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
| Non-provider secret patterns | Unavailable | Requires GitHub Advanced Security, which this free public repo does not have; the API accepts the setting and silently leaves it disabled - **gitleaks covers this gap** |
| gitleaks | Push, PR, weekly full history | Catches the *generic* secrets provider patterns miss: a Socrata token, a hand-written connection string, an Azure key pasted into a template |
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

### F6 - Docs referenced Key Vault, which nothing provisioned

**Why it happened:** the zero-secrets design (D14) named Key Vault as the
production secret store, and five documents repeated it. The provisioning
script never created one - correctly, as it turns out: Managed Identity removed
the SQL password and OIDC removed the deploy credential, leaving only the
*optional* Socrata token, so a vault would have been provisioned empty. The
docs described an intention the implementation had outgrown.

**Third instance of the same pattern** (see F3, F5): a control named in
documentation but absent in reality. Here the implementation was right and the
documentation was stale - the opposite direction to F3 and F5, and a reminder
that drift runs both ways.

**Fix:** the token now has a real home - an App Service application setting by
default (encrypted at rest, injected as `Feeds__SocrataAppToken`), with
`-WithKeyVault` provisioning a vault and switching that setting to a
`@Microsoft.KeyVault(SecretUri=...)` reference resolved by the app's managed
identity. Documentation across five files now describes what exists, with the
Key Vault path presented as opt-in demonstration rather than a requirement.
No token value appears in the repository or its documentation.

### F5 - The documented user-secrets path could not be executed

**Why it happened:** the docs instruct developers to store the optional Socrata
token with `dotnet user-secrets set` - the right mechanism, keeping the value
in the user profile entirely outside the repository. But no `UserSecretsId` was
ever added to `Lottery.Api.csproj`, and without it that command simply fails.
The secure path was documented and never run.

This is the same failure as F3, found later: **a security instruction is a
claim, and an unexecuted claim is not a control.** Two instances of one pattern
is what makes it a habit worth naming (lesson 21).

**Fix:** `UserSecretsId` added to the project and the documented command
verified end to end - it now succeeds, writes to the user profile, and leaves
the working tree clean. The id itself is a folder name, not a secret, and is
committed deliberately so the command works for anyone who clones.

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

0. **NuGet updates must regenerate the lock files.** Dependabot updates
   `Directory.Packages.props` only; run `dotnet restore --force-evaluate` on
   the PR branch and commit the `packages.lock.json` changes, or CI's
   locked-mode restore fails with `NU1004`
   ([lesson 20](LESSONS-LEARNED.md#20-dependabot-prs-failed-locked-mode-restore)).
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
5. **Framework majors are migrations, not bumps.** Angular majors require
   `ng update`, which migrates source code alongside packages - per-package
   PRs (e.g. `@angular/common` 20 -> 22 alone) cannot succeed even if all of
   them merged, so they are closed in favour of a single deliberate migration
   PR when the upgrade is chosen.

### Triage of the initial backlog (2026-07-28)

All 20 open dependency PRs were resolved when this policy was adopted:
**10 merged** on genuinely green CI (the grouped NuGet minor/patch set, every
GitHub Actions bump on both branches, and the npm patch group);
**6 closed with recorded rationale** (the `Microsoft.OpenApi` pin per rule 4,
four per-package Angular majors per rule 5, and one SqlClient PR superseded by
the grouped update - its leftover `dbup-sqlserver` major will return as its
own PR); and the `Microsoft.Data.SqlClient` 7.x major was **verified locally**
(build + all 68 tests) against the `SqlConfigurableRetryFactory` wake-up path
before any of it was trusted. The triage also surfaced lesson 20 above: the
first NuGet PR through the new CI failed locked-mode restore, which is exactly
the kind of latent gap a first real run exists to find.

## Reversed decision: gitleaks is now in CI

It was originally listed here as deliberately skipped, on the grounds that
push protection covered the same ground. **That reasoning was wrong once F1
established what the free tier actually provides.** GitHub's free secret
scanning matches ~200 *known vendor* formats; generic secrets are covered only
by non-provider patterns, which need Advanced Security we do not have. The
realistic leak candidates here are all generic: the optional Socrata token, a
hand-written connection string, an Azure key.

Timing drove the decision as much as coverage. Azure provisioning introduces
Bicep templates, deploy scripts and workflow YAML - files that *must* be
committed, so `.gitignore` offers no protection, and precisely where a value
gets pasted "just to test it". A scanner added after that work would never
have watched the commits where the risk was highest, so it went in first.

Configuration lives in `.gitleaks.toml`, with an allowlist for connection
string *shapes* that appear in documentation and code without containing a
value (the architecture uses Managed Identity, so a production connection
string has no password in it at all).

## What is deliberately not done
- **SHA-pinning actions.** Tag pinning plus Dependabot keeps actions current;
  SHA pinning trades that automation for supply-chain rigidity that this
  repository's threat model does not justify.
- **A CSPRNG for pick generation.** Reviewed and rejected with reasons - see
  [decision D21](REQUIREMENTS-AND-DECISIONS.md#external-review-analyzed-why-not-a-csprng-d21).
