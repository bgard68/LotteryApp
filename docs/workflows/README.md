# GitHub workflows

What runs automatically on this repo and why. Workflow definitions live in
[`.github/workflows/`](../../.github/workflows/); this page documents them.

Back to the [main README](../../README.md).

## Which workflows live on which branch

`main` and `frontend` are never merged, so **each branch carries only the
workflows that can actually fire there.** A workflow triggering on
`branches: [frontend]` is inert if it sits on `main`, and a `schedule` trigger
fires **only from the default branch** regardless of which branches hold the
file. Duplicating them across branches leaves dead files that silently diverge
from the live copy.

| Workflow | `main` | `frontend` | Why |
|---|---|---|---|
| `ci.yml` | yes | yes | Each branch tests its own pushes and PRs |
| `codeql.yml` | yes (C#) | yes (JS/TS) | Different language per branch - the Angular app only exists on `frontend` |
| `gitleaks.yml` | yes | yes | Each branch scans its own commits |
| `ci-frontend.yml` | - | yes | Angular specs, build, OpenAPI drift check |
| `deploy-api.yml` | yes | - | The API deploys from `main` only |
| `deploy-web.yml` | - | yes | The frontend deploys from `frontend` only |
| `era-check.yml` | yes | - | Scheduled: fires only from the default branch |
| `keep-alive.yml` | yes | - | Scheduled: same |
| `cleanup-runs.yml` | yes | - | Scheduled, and runs are repo-wide |

## Current

### CodeQL (`codeql.yml`)

C# static security analysis. Triggers: every push and PR to `main`, plus a
**weekly schedule** - the cron matters because security alerts should surface
even when nobody is committing. Uses `build-mode: none` (no compilation
needed for C# analysis), so it stays fast and needs no .NET setup step.

### Dependabot (`../dependabot.yml`)

Weekly dependency update PRs for two ecosystems:

- **NuGet** - minor/patch updates arrive grouped into a single PR to cut
  noise; majors stay individual PRs. Because this repo uses Central Package
  Management, every bump touches only `Directory.Packages.props`.
- **GitHub Actions** - keeps the workflow actions themselves current.

Dependabot *alerts* and *automated security fixes* are also enabled at the
repo-settings level, so a disclosed vulnerability opens a fix PR without
waiting for the weekly schedule.

### CI (`ci.yml`)

On every push to `main` and `frontend`: locked restore, build with warnings
as errors, the full backend test suite, then an **end-to-end smoke test
against a real running instance** - the API boots on the runner (migrates
SQLite, seeds from the committed snapshots, all offline) and
`scripts/smoke-test.ps1` exercises every endpoint including the error
conditions.

### CI frontend (`ci-frontend.yml`) - `frontend` branch

The Angular app never merges into `main`, so this file lives only there: `npm ci`,
all specs on headless Chrome, a production build, and the **OpenAPI client
drift check** - the committed `schema.d.ts` is regenerated from the running
API's OpenAPI document and any diff fails the build, so the frontend can
never quietly disagree with the backend contract (decision D20).

### Era check (`era-check.yml`)

Weekly (Mondays 06:00 UTC) + manual: runs the full test suite - including
the era-coverage test over all 4,493 committed drawings - then boots the API
and triggers a live refresh cycle, failing if the live feed produced any
era-invalid draws. This is what turns "the lottery changed its rules" into a
red run within days instead of a silent mis-validation (decision D10,
lessons-learned #6).

### Keep alive (`keep-alive.yml`)

Scheduled around draw times (cron pairs cover both EDT and EST): warms
`/healthz` before each drawing and triggers `POST /internal/refresh` after,
so results land promptly without paying for App Service Always On (decision
D16). **No-op until the `API_BASE_URL` repo variable is set** - safe to have
enabled before anything is deployed. Sends the optional `X-Refresh-Key`
header from the `REFRESH_KEY` secret when configured.

## Deployments (live)

Both halves deploy automatically and independently - a backend change never
redeploys the frontend, and vice versa (decision D15). Authentication is OIDC
federation, so no deployment credential is stored anywhere (D14).

| Workflow | Branch | Fires on | What it does |
|---|---|---|---|
| `deploy-api.yml` | `main` | `src/**`, `Directory.*.props`, `global.json`, the smoke test, or itself | Test -> publish (linux-x64, ~52 MB) -> OIDC login -> App Service deploy -> **`smoke-test.ps1` against the live URL as the gate** (D17) |
| `deploy-web.yml` | `frontend` | `lottery-web/**` or itself | Install -> specs -> build -> write `config.json` from `API_BASE_URL` -> deploy to Static Web Apps |

Both keep `workflow_dispatch` for manual runs.

**The gate matters.** A failed smoke test fails the deployment run, so a broken
deploy is known immediately rather than when a visitor finds it. It has already
earned its place: it caught the API returning 503 across every endpoint when a
missing directory sent the app into a restart loop
([lesson 25](../LESSONS-LEARNED.md)).

**Path filters are deliberate.** Documentation changes, decision records and
lesson write-ups touch `main` constantly; none of them should redeploy a
running API.

**Configured by provisioning.** `./scripts/provision-azure.ps1` sets every
variable these workflows read (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`, `API_APP_NAME`, `API_BASE_URL`) and the one secret
(`AZURE_STATIC_WEB_APPS_API_TOKEN`). Re-running it is safe.

## Cleanup (`cleanup-runs.yml`)

Weekly (Sundays 04:00 UTC) + manual. Prunes Actions history **by count rather
than age** - GitHub's built-in retention is time-based (90 days maximum), which
says nothing useful when a busy week produces fifty runs and a quiet one
produces two.

Defaults: **5 runs per workflow**, **2 per deployment workflow** - a deploy's
history is only interesting for "what is live" and "what did the previous one
do". Both are overridable when running it manually.

Safety: only `completed` runs are eligible, so nothing in flight is touched -
including the cleanup run itself. It lives on `main` only, since runs are
repo-wide (one copy prunes the frontend branch's workflows too) and scheduled
triggers fire only from the default branch. Each run writes a summary table of
what remains.

## Conventions

- Workflows get **least-privilege permissions** blocks (`contents: read` by
  default; CodeQL adds only `security-events: write`).
- No secrets in workflow files - deploys authenticate via OIDC federation,
  and anything else arrives as `${{ secrets.NAME }}` references by name.
