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

## Planned (Phase 4)

| Workflow | Purpose |
|---|---|
| `deploy-api.yml` | Path-filtered (`src/**`): build, test, deploy the API to Azure App Service via **OIDC** (no stored credentials), then run `scripts/smoke-test.ps1` against the live URL as a **deploy gate** - a failed smoke test fails the deployment |
| `deploy-web.yml` | Path-filtered (`lottery-web/**`): build and deploy the Angular app to Azure Static Web Apps - frontend and backend deploy independently |
| `keep-alive.yml` | Scheduled pings to `/healthz` around draw times so the App Service is awake to fetch results (free-tier alternative to Always On) |
| `era-check.yml` | Weekly run of the era-coverage test against the live feed, so a future lottery rule change surfaces within days, not on the next commit |

## Conventions

- Workflows get **least-privilege permissions** blocks (`contents: read` by
  default; CodeQL adds only `security-events: write`).
- No secrets in workflow files - deploys authenticate via OIDC federation,
  and anything else arrives as `${{ secrets.NAME }}` references by name.
