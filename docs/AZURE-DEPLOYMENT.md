# Azure deployment (free tier)

How this app is provisioned and deployed for **$0/month**, what the free tier
forces us to change, and why no secret is stored anywhere in the process.

Back to the [main README](../README.md).

## One command

```powershell
./scripts/provision-azure.ps1 -WhatIf   # see the plan
./scripts/provision-azure.ps1           # create it
```

Prerequisites: `az login` and `gh auth login`. That is the entire input - the
script **discovers everything else**: subscription and tenant from your Azure
login, the repository from `remote.origin.url`, the region from your existing
resource groups. Nothing is hardcoded, and re-running is safe (every resource
is checked before it is created).

## What it creates, and why each is free

| Resource | SKU | Cost | Note |
|---|---|---|---|
| Resource group | - | $0 | Tagged `app=lottery` so the script can find its own work |
| App Service plan | **F1** | $0 | 60 CPU-minutes/day, 1 GB RAM, **no Always On** |
| App Service (API) | - | $0 | Linux, .NET 10 |
| Static Web App (frontend) | **Free** | $0 | 100 GB bandwidth/month |
| Database | SQLite on `/home` | $0 | Persistent volume; default |
| Database (alternative) | Azure SQL free offer | $0 | `-Database AzureSql`; serverless General Purpose, one per subscription |
| Managed identity | - | $0 | The app's own Azure identity |
| Entra app + OIDC federation | - | $0 | Deployment identity with no stored credential |

Tear it all down with `az group delete --name rg-lottery`.

## What the free tier forces us to change

Two design decisions from the original plan do not survive contact with the
free SKUs. Both are recorded here rather than quietly worked around.

### 1. No linked backend, so CORS is required

**The plan** ([D15](REQUIREMENTS-AND-DECISIONS.md)) used the Static Web Apps
*linked backend* feature: SWA proxies `/api/*` to the App Service, the browser
sees one origin, and CORS never enters the picture.

**The reality:** linked backends are a **Standard-tier** feature. On the Free
SKU the browser must call the API's own origin, which means cross-origin
requests, which means the API must explicitly allow the frontend's origin.

**What we do:** the provisioning script sets `Cors__AllowedOrigins__0` to the
Static Web App's URL, and the API adds a CORS policy driven entirely by
configuration - empty locally, where the dev proxy makes everything
same-origin anyway. Independent deployment (requirement 12) is preserved,
which was the point of the two-host split in the first place. Upgrading SWA to
Standard later restores the no-CORS design by deleting configuration, not code.

### 2. No Always On, so the keep-alive workflow matters

F1 cannot keep a process resident. This was already anticipated
([D16](REQUIREMENTS-AND-DECISIONS.md)): the keep-alive workflow pings
`/healthz` around draw times and triggers `POST /internal/refresh` after each
drawing, and the refresh cycle is self-healing on startup regardless. The
free tier makes that workflow load-bearing rather than an optimisation.

## Secrets: there are none to store

The whole pipeline runs without a stored credential:

- **Deployment authenticates via OIDC federation.** The script creates an Entra
  app registration with federated credentials for `main`, `frontend`, and pull
  requests. GitHub presents a short-lived token proving "I am this workflow on
  this ref"; Azure trusts the issuer. No client secret is ever created, so
  none can leak.
- **The API reaches Azure SQL via Managed Identity.** With `-Database AzureSql`
  the server is created with **Entra-only authentication** - no SQL admin
  password exists at all, and the connection string contains no credential.
- **Role assignment is scoped to the resource group**, not the subscription.
- **The one genuine secret** - the Static Web Apps deployment token - is piped
  straight from `az` into `gh secret set`. It is never written to a file, never
  printed, and is removed from memory immediately after.
- **Non-sensitive identifiers** (client id, tenant id, subscription id, app
  name, URLs) go to GitHub **variables**, which are visible by design.

## After provisioning

The script sets every repository variable the workflows need. Then:

1. Uncomment the push trigger in `.github/workflows/deploy-api.yml` (on `main`)
   and `.github/workflows/deploy-web.yml` (on `frontend`).
2. Push or run those workflows manually.
3. The API deploy runs `scripts/smoke-test.ps1` against the live URL as its
   gate - a failed smoke test fails the deployment
   ([D17](REQUIREMENTS-AND-DECISIONS.md)).

## Guarding against accidents

Provisioning and deployment introduce the file types where credentials get
pasted by accident - Bicep templates, deploy scripts, workflow YAML - all of
which are *meant* to be committed, so `.gitignore` cannot help. That is why
[gitleaks](SECURITY-POSTURE.md) scans every push and PR plus a weekly
full-history sweep, and why it was added **before** this Azure work rather
than after.
