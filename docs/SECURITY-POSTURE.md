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

### F7 - HTTPS was not enforced on the App Service

`httpsOnly` was `false`, and `http://app-lottery-.../healthz` returned **200
over plaintext** rather than redirecting. Nothing secret travels this API - it
serves public lottery results with no authentication - so the exposure was
limited, but an unencrypted endpoint is also the first thing a reviewer checks.

**Fix:** `az webapp update --https-only true`. Plain HTTP now answers **301** to
the HTTPS URL. No code change was needed; the platform does the redirect before
the app sees the request.

### F8 - The refresh endpoint was unauthenticated in production

`POST /internal/refresh` supports a shared-key guard (`X-Refresh-Key` matched
against `Refresh:Key`), and the keep-alive workflow sends the header when the
secret exists. But `Refresh:Key` was set **nowhere** - not in `appsettings`,
not as an App Service setting - so `requiredKey` was empty and the guard was
skipped entirely. Anyone could trigger feed fetches and database writes.

**Fix:** a 64-character random key generated, stored as the App Service setting
`Refresh__Key` and as the `REFRESH_KEY` GitHub Actions secret in the same
operation so the two cannot drift. The value was never printed or written to
disk. Verified: an unauthenticated POST now returns **401**, and a manually
dispatched keep-alive run succeeds, proving the secret matches end to end.

### F9 - The rate limiter was partitioning on the wrong IP address

The limiter keyed on `context.Connection.RemoteIpAddress` with no
forwarded-headers handling. Behind App Service that address is the platform
front end, not the visitor - so every caller in the world shared **one**
partition. The practical effect is the opposite of the intent: no per-client
limiting at all, and one busy client able to 429 the entire site. It also made
F8 worse, because the rate limit was the only thing standing in front of the
unauthenticated refresh endpoint.

**Fix:** `UseForwardedHeaders` first in the pipeline, with `ForwardLimit = 1`.
That reads the **rightmost** `X-Forwarded-For` entry - the one the front end
appends - so a client sending its own header only adds entries to the left and
cannot forge an identity to dodge the limit. `KnownNetworks`/`KnownProxies` are
cleared because the front end's address is neither stable nor knowable.

### Not fixed: secret-scanning non-provider patterns and validity checks

Both were requested via the API and both silently stayed `disabled` - they are
GitHub Secret Protection features, not part of what a public repository gets
for free. Basic secret scanning and push protection are enabled and are the
controls that matter here. Recorded so the gap is not rediscovered as a bug.

## Senior DevOps / security review (2026-07-28)

A second pass, this time reviewing the **running infrastructure** rather than
the code: OIDC trust, RBAC scope, diagnostics, response headers, workflow
supply chain. Findings D1-D10; the ones with an attack story are first.

### D1 - An unused OIDC credential granted Contributor to any PR run

Six federated credentials existed on the deploy app registration, two of them:

```
pull-request            repo:bgard68/LotteryApp:pull_request
pull-request-immutable  repo:bgard68@.../LotteryApp@...:pull_request
```

That principal held **Contributor on the whole resource group**, and *no
workflow used OIDC on `pull_request`* - `deploy-api.yml` triggers only on push
to `main`. So it was a standing grant of resource-group-wide write to any
workflow running in a PR context, for no benefit at all.

GitHub withholds `id-token` from fork PRs, which caps the blast radius at
branches inside the repo. That is a platform behaviour protecting us, not a
control we chose - and relying on it is exactly the kind of assumption this
review exists to find.

**Fix:** both credentials deleted. Four remain, each pinned to a specific
branch ref.

**Prevention:** provisioning creates credentials per *trigger the workflows
actually use*. Adding a `pull_request` credential is now a deliberate act that
has to be justified, not a default.

### D2 - Contributor was broader than the deploy needed

The same principal had `Contributor` at `/resourceGroups/rg-lottery` - enough to
delete the App Service plan, the Static Web App, and anything added later. The
workflow publishes a zip to one web app.

**Fix:** `Website Contributor`, scoped to the web app resource. The
resource-group `Contributor` assignment was removed.

**Verified, not assumed:** a `deploy-api` run was dispatched immediately after
the change and succeeded end to end, including the smoke-test gate. A narrowed
permission that silently breaks deploys is worse than the permission.

### D3 - App Service diagnostics were entirely off

```
applicationLogs = Off   httpLogs = False   detailedErrors = False
```

No persistent diagnostic trail existed. This is the same blind spot that made
[lesson 25](LESSONS-LEARNED.md) take a day: the crash loop was diagnosed from
*restart counts*, because there were no logs to read.

**Fix:** filesystem application logging at Warning, HTTP logging, detailed
errors and failed-request tracing, all enabled. Free at this tier.

**Caveat recorded:** App Service auto-disables filesystem logging after 12
hours. It is the right control for "something is wrong now"; a durable trail
needs a storage-account sink, which is the next step if this ever matters more.

### D4 - The deployed site had no CSP and no anti-framing

There was **no `staticwebapp.config.json` at all**. Azure supplies HSTS,
`Referrer-Policy` and `X-Content-Type-Options` by default; everything else was
absent. The site could be framed by any page, with no defence in depth against
injected script.

**Fix:** a config declaring CSP, `X-Frame-Options`, `Permissions-Policy` and a
navigation fallback the SPA never had.

**Guarded by a test, and pointed at the build output:** `npm run check:swa`
runs in CI against `dist/`, not the source tree. The config only reaches Azure
if the asset pipeline copies it - and a site deployed without it **works
perfectly**. Nothing else in the suite would have noticed. The check was
verified in both directions: green on the real build, and failing with a
precise message when a header is deleted.

### D5 - Deploy workflows could race

No `concurrency:` anywhere. Two merges in quick succession could deploy out of
order, with the older build winning.

**Fix:** a concurrency group per deploy workflow, `cancel-in-progress: false` -
serialise rather than cancel, because a half-finished deploy is worse than a
queued one.

### D6 - The API returned no security headers

Responses carried only `Server: Kestrel` - free version disclosure, and nothing
else.

**Fix:** `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
`Cross-Origin-Resource-Policy` and (outside Development) a
`default-src 'none'; frame-ancestors 'none'` CSP, plus HSTS. The Kestrel server
header is suppressed.

Placement matters and is commented in code: the middleware sits **before**
everything that can short-circuit - CORS preflight, the rate limiter's 429, the
endpoints' 400s - so error responses carry the headers too. The CSP lockdown is
skipped in Development because Scalar is a real HTML page and `'none'` would
break it.

**Guarded by five smoke-test assertions**, including one that asserts the
*absence* of `Server`. The suite went 28 -> 32 checks.

### D7 - Third-party action on a moving tag

`gitleaks/gitleaks-action@v2` runs on every push and PR, and `@v2` can be
repointed at any time by its maintainer.

**Fix:** pinned to a commit SHA on both branches.

### D8 - 12 npm advisories, all dev-only (not fixed, deliberately)

Production dependencies are clean; the affected packages are Angular's karma
test tooling, which never reaches a browser. `npm audit fix --force` would
rewrite the test runner to resolve advisories with no production exposure.
Revisit when Angular updates the chain.

### D9 - No rollback path (accepted)

F1 has no deployment slots, so there is no blue/green and no instant swap-back.
Rollback means reverting a commit and waiting for a deploy - a few minutes,
gated by the smoke test. Acceptable at this tier; recorded so it is a decision
rather than a discovery.

### D10 - No backup, and that is genuinely fine (accepted)

SQLite on `/home` has no backup and F1 offers none. The data is fully
reconstructible: committed JSON snapshots plus a Socrata re-fetch, with the
`ImportLedger` making re-seeding idempotent. Effective RPO is zero **by
design** - but nothing recorded that reasoning, so a reader could not tell
whether it had been considered or missed. Now it is written down.

### Bugs hit while fixing these

- **`az role assignment create` failed with `MissingSubscription`** under Git
  Bash even with an explicit `--scope`. The same command from PowerShell worked.
  A shell/auth-context quirk, not an Azure one - worth knowing before concluding
  a permission model is broken.
- **A verification ran against the wrong environment.** Testing the new CSP with
  `ASPNETCORE_ENVIRONMENT=Production dotnet run` showed *no* CSP - because
  `dotnet run` applies `launchSettings.json`, which forces Development and
  overrides the environment variable. `--no-launch-profile` gave a true
  Production run and the header appeared. The header was never missing; the test
  was. **Lesson:** when a security control appears absent, confirm the
  environment before changing the code.

### D11 - Frontend code review (the first pass covered config, not code)

The initial review examined the frontend's *infrastructure* - headers, deploy
concurrency, action pinning, npm advisories - but not its **code**. A second
pass looked for DOM sinks and what actually ships in the bundle.

**Clean, and verified rather than assumed:**

| Check | Result |
|---|---|
| `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` | none |
| `bypassSecurityTrust*`, `DomSanitizer` | none |
| `eval`, `new Function` | none |
| `localStorage`, `sessionStorage`, `document.cookie` | none - nothing persisted client-side |
| `target="_blank"` without `rel` | none |
| Source maps in the production bundle | none emitted (`sourceMap` is scoped to the `development` config) |
| Secret-shaped strings in the shipped JS | none |
| TypeScript strictness | `strict`, `strictTemplates`, `strictInjectionParameters`, `typeCheckHostBindings` all on |
| Production dependencies | 8, all first-party Angular + rxjs + tslib, 0 advisories |

There is no XSS surface because nothing bypasses Angular's default escaping -
the app renders numbers and dates through interpolation only.

**One finding: URLs built by string interpolation.**

```ts
`/api/${game}/check?whites=${whites.join(',')}&special=${special}`
```

The numbers are validated upstream. The **game** is not: it reaches the store
through `store.setGame($any($event.target).value)`, and `$any` is a deliberate
hole in type checking, so at runtime only the `<select>` options constrain it.
A path segment is where that matters - unencoded, a crafted value changes
*which* endpoint is called rather than what is asked of it.

**Honest severity: low.** The exposure is the user's own browser and the server
404s unknown games. Hygiene, not a live vulnerability.

**Fix:** `encodeURIComponent` for path segments, `HttpParams` for query values -
encoding by construction, because "remember to encode" is not a control.

**Test:** `http-lottery-api.spec.ts` - the one place in the suite where mocking
HTTP is correct, because this class exists to speak HTTP, so the request it
builds *is* the behaviour under test. It pins the encoding with a game value
that tries to traverse out of its segment, and covers the
429 / unreachable / genuine-500 classification that had no direct test.
Specs 45 -> 53.

### Deployment status of these fixes

| Fix | Merged | Live |
|---|---|---|
| D1 unused OIDC credential deleted | n/a (Azure) | **yes**, verified |
| D2 Contributor -> Website Contributor | n/a (Azure) | **yes**, proven by a deploy |
| D3 diagnostics enabled | n/a (Azure) | **yes**, config re-read |
| D6 API security headers | yes | **yes**, all six verified on the live API |
| D5 concurrency, D7 action pinning | yes | yes |
| D4 frontend CSP + anti-framing | yes | **not yet** - see below |
| D11 URL encoding | yes | **not yet** - same deploy |

The frontend deploy failed three consecutive times on an **Azure-side** error:

```
Using 'staticwebapp.config.json' file for configuration information
...
We are currently experiencing problems communicating with our content server.
Please try again later or file an issue if this behavior continues.
```

Azure accepted the config and then failed inside its own content service. The
App Service deploy succeeded in the same window, so this is Static Web Apps
specific rather than a credential or subscription problem. The live site
continues to serve the previous build intact - a failed SWA upload leaves the
existing version in place rather than a partial one.

**Until it deploys, the site keeps Azure's defaults** (HSTS, `Referrer-Policy`,
nosniff) and lacks the CSP. Re-run `deploy-web.yml` once the service recovers;
no code change is needed.

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
