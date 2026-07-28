# GitHub workflows

What runs automatically on this repo and why. Workflow definitions live in
[`.github/workflows/`](../../.github/workflows/); this page documents them.

Back to the [main README](../../README.md).

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

### CI frontend (`ci-frontend.yml`)

`frontend` branch only (the Angular app never merges into `main`): `npm ci`,
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

## Deploy skeletons (manual-only until Azure exists)

| Workflow | Purpose |
|---|---|
| `deploy-api.yml` | Test -> publish -> **OIDC** login (no stored credential, decision D14) -> App Service deploy -> `smoke-test.ps1` against the live URL as the **deploy gate** (D17). Push trigger (path-filtered `src/**`) is committed but commented out until the App Service + federation exist |
| `deploy-web.yml` | Specs -> deploy to Azure Static Web Apps (linked backend proxies `/api/*`, no CORS - D15). Lives on the `frontend` branch; push trigger (path-filtered `lottery-web/**`) commented out until the SWA exists |

Activation checklist when Azure is provisioned: set repo variables
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
`API_APP_NAME`, `API_BASE_URL`, secret `AZURE_STATIC_WEB_APPS_API_TOKEN`
(and optionally `REFRESH_KEY`), then uncomment both push triggers.

## Conventions

- Workflows get **least-privilege permissions** blocks (`contents: read` by
  default; CodeQL adds only `security-events: write`).
- No secrets in workflow files - deploys authenticate via OIDC federation,
  and anything else arrives as `${{ secrets.NAME }}` references by name.
