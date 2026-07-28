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

## The one optional secret: the Socrata feed token

The app needs no token - it only raises NY Open Data rate limits, and this
app's traffic sits far below the anonymous ceiling. If you have one
([how to obtain it](DATA-SOURCES.md#where-to-get-one)), the script gives it a
real production home:

```powershell
# default: App Service application setting
./scripts/provision-azure.ps1 -SocrataToken (Read-Host -AsSecureString)

# opt-in: Key Vault, with the app setting holding only a reference
./scripts/provision-azure.ps1 -SocrataToken (Read-Host -AsSecureString) -WithKeyVault
```

> **These are alternatives, not a fallback chain.** You choose one at
> provisioning time; there is no automatic failover between them. (The word
> "fallback" appears elsewhere in these docs for the *jackpot feeds*, where a
> second source really is tried when the first fails - that mechanism has
> nothing to do with secret storage.) Pass no token at all and neither setting
> is created: the app calls Socrata anonymously, which is fine at a few
> requests per week.

**Default (application setting).** The value lives in the App Service's own
configuration store - encrypted at rest, outside source control. Azure injects
it as the environment variable `Feeds__SocrataAppToken`, which .NET reads as
the config key `Feeds:SocrataAppToken` (a double underscore stands in for the
colon, since environment variables cannot contain one). No extra resources, no
cost.

Where to see it after deployment:

- **Portal:** your App Service -> Settings -> **Environment variables** -> App
  settings -> `Feeds__SocrataAppToken`
- **CLI:** `az webapp config appsettings list --name <api-app-name> --resource-group rg-lottery`

**`-WithKeyVault`.** Provisions a Key Vault with RBAC authorization, grants the
app's managed identity the *Key Vault Secrets User* role (read-only), stores
the secret, and sets the app setting to `@Microsoft.KeyVault(SecretUri=...)`.
App Service resolves that reference at startup using the managed identity.

Where things live in this mode:

- **The value:** Key Vault -> Secrets -> `Feeds--SocrataAppToken` (hyphens
  because vault secret names allow only letters, digits and hyphens)
- **The app setting:** still `Feeds__SocrataAppToken`, but its value is the
  `@Microsoft.KeyVault(...)` reference rather than the token

### The difference that actually matters

Both options encrypt at rest, so that is not the deciding factor. The real
distinction is **who can read the value**:

| | Plain app setting | Key Vault reference |
|---|---|---|
| Someone with read access to the App Service (e.g. Contributor) | **Sees the token** in the Portal or via CLI | Sees only the reference URI |
| Reading the actual value | - | Requires *separate* RBAC on the vault |
| Access record | None | Every read is audit-logged |
| Rotation | Edit the setting, restart | Add a new secret version |

That separation of duties - not encryption - is what Key Vault buys.

Which to choose: this architecture does not *need* it. Managed Identity and
OIDC already eliminate every other secret, so a vault here holds exactly one
optional rate-limit token. The switch exists because it is the textbook pattern
and worth demonstrating; the cost is fractions of a cent per month at this
volume. The application code is identical either way, which is the point:
storage is a deployment decision, not a code decision.

Handling: the script takes the token as a **SecureString**, converts it at the
last possible moment, clears it from memory immediately, and never writes it to
a file or prints it. No token value appears anywhere in this repository.

## Secrets: there are none you must store

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

## What actually gets deployed

`dotnet publish` emits build output and declared content only - the repository
itself is never uploaded. Verified against a real publish:

| | Files | Size |
|---|---|---|
| Default publish | 112 | 130 MB |
| `-r linux-x64 --self-contained false` (what the workflow does) | 72 | **52 MB** |

The saving is entirely native SQLite binaries for platforms this app will never
run on - android-arm, ios-arm64, browser-wasm, linux-s390x and about thirty
more, bundled by SQLitePCLRaw for every platform by default. Targeting the App
Service's actual platform keeps only `libe_sqlite3.so`. That matters on F1,
whose storage is shared with the SQLite database on `/home`.

What ships: four assemblies plus dependencies, `appsettings*.json`,
`web.config`, runtime config, and the native SQLite library. What does not:
source files, the local `lottery.db`, docs, scripts, project files, `.git` -
all confirmed absent. **PDBs are kept deliberately** - on a free tier with no
APM attached, readable production stack traces are worth a few hundred KB.

There is no `wwwroot`: the API serves no static files. The Angular app deploys
separately to Static Web Apps, which is the point of the two-host split.

## Startup failures do not restart-loop

App Service restarts a container that exits, so a configuration error in
startup work becomes an unbounded loop - one missing directory once consumed
this plan's entire 60-minute daily CPU quota in about fifteen minutes across
51 restarts ([lesson 25](LESSONS-LEARNED.md)).

The app therefore:

- **creates its SQLite parent directory** before connecting (SQLite makes
  files, never directories);
- **catches startup database failures**, logs `LogCritical` including the
  connection string in use, and starts anyway - failing loudly and *once*;
- **reports the truth on `/healthz`**, which runs a real query rather than
  answering "OK" merely because the process is alive. Unhealthy is what the
  deploy gate, the keep-alive workflow and Azure all watch.

If the quota is exhausted, Azure returns **HTTP 403 "Web App - Unavailable"**
from its own infrastructure - the app never sees the request. Check with:

```bash
az webapp show --name <api-app-name> --resource-group rg-lottery   --query "{state:state, usage:usageState}" -o tsv
```

`QuotaExceeded` resets daily at midnight UTC. Restart counts, which reveal a
loop immediately, come from the site's `usages` endpoint as `WPStopRequests`.

## Guarding against accidents

Provisioning and deployment introduce the file types where credentials get
pasted by accident - Bicep templates, deploy scripts, workflow YAML - all of
which are *meant* to be committed, so `.gitignore` cannot help. That is why
[gitleaks](SECURITY-POSTURE.md) scans every push and PR plus a weekly
full-history sweep, and why it was added **before** this Azure work rather
than after.
