#Requires -Version 5.1
<#
.SYNOPSIS
    Provisions the free-tier Azure resources for LotteryApp and wires up
    GitHub OIDC deployment - without storing a single secret anywhere.

.DESCRIPTION
    Everything this script needs is DISCOVERED, never hardcoded: the
    subscription and tenant come from your az login, the repository from the
    git remote, and existing resources are reused rather than recreated
    (safe to re-run).

    Free tier throughout:
      * App Service plan  F1  (free; 60 CPU-minutes/day, no Always On - which
                               is why the keep-alive workflow exists)
      * Static Web App    Free
      * Database          SQLite on the App Service's persistent /home volume
                          ($0), or -Database AzureSql to use Azure SQL's free
                          serverless offer instead
      * Managed identity, resource group, OIDC federation - all free

    SECRETS: none are written to disk, echoed, or committed. Deployment uses
    OIDC federated credentials (GitHub proves its identity per run, no stored
    credential). The one real secret - the Static Web Apps deployment token -
    is piped straight from az into `gh secret set` and never touches a file
    or the console.

.PARAMETER Name
    Base name for resources. Default: lottery. A short deterministic suffix
    derived from the subscription id is appended where global uniqueness is
    required.

.PARAMETER Location
    Azure region. Default: discovered from your existing resource groups, or
    eastus2 if you have none.

.PARAMETER Database
    Sqlite (default, $0) or AzureSql (uses the free serverless offer - one per
    subscription; the script fails clearly if that offer is unavailable).

.PARAMETER WhatIf
    Print the plan without creating anything.

.EXAMPLE
    ./scripts/provision-azure.ps1 -WhatIf
    ./scripts/provision-azure.ps1
    ./scripts/provision-azure.ps1 -Database AzureSql
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Name = "lottery",
    [string]$Location,
    [ValidateSet("Sqlite", "AzureSql")]
    [string]$Database = "Sqlite"
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host "    $Message" -ForegroundColor Gray }

function Invoke-Az {
    <# az with JSON output, converted, failing loudly. #>
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & az @Arguments --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') failed:`n$output"
    }
    if ([string]::IsNullOrWhiteSpace($output)) { return $null }
    return $output | ConvertFrom-Json
}

function Test-AzResource {
    <# Existence check that treats "not found" as false rather than an error. #>
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & az @Arguments --output none 2>$null
    return ($LASTEXITCODE -eq 0)
}

# ---------------------------------------------------------------- preflight
Write-Step "Preflight"

foreach ($tool in @("az", "gh", "git")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is not installed or not on PATH. Install it and re-run."
    }
}
Write-Ok "az, gh and git found"

$account = try { Invoke-Az account show } catch { $null }
if (-not $account) { throw "Not logged in to Azure. Run: az login" }

$subscriptionId = $account.id
$tenantId       = $account.tenantId
Write-Ok "Subscription: $($account.name) ($subscriptionId)"

& gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Not logged in to GitHub. Run: gh auth login" }

# Repository discovered from the remote - never hardcoded.
$remoteUrl = (& git config --get remote.origin.url).Trim()
if ($remoteUrl -notmatch "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)") {
    throw "Could not parse a GitHub owner/repo from remote.origin.url ($remoteUrl)."
}
$repoOwner = $Matches.owner
$repoName  = $Matches.repo
$repoSlug  = "$repoOwner/$repoName"
Write-Ok "Repository: $repoSlug"

# Location: reuse whatever the subscription already favours, else a default.
if (-not $Location) {
    $existing = Invoke-Az group list --query "[0].location"
    $Location = if ($existing) { $existing } else { "eastus2" }
}
Write-Ok "Location: $Location"

# Deterministic short suffix so globally-unique names are stable across re-runs.
$hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash(
    [System.Text.Encoding]::UTF8.GetBytes("$subscriptionId/$repoSlug"))
$suffix = -join ($hash[0..3] | ForEach-Object { $_.ToString("x2") })

$resourceGroup = "rg-$Name"
$planName      = "asp-$Name"
$apiName       = "app-$Name-$suffix"      # globally unique
$swaName       = "swa-$Name-$suffix"      # globally unique
$identityName  = "id-github-$Name"

Write-Info "Resource group : $resourceGroup"
Write-Info "App Service    : $apiName (F1 free)"
Write-Info "Static Web App : $swaName (Free)"
Write-Info "Database       : $Database"

if ($WhatIfPreference) {
    Write-Host "`n-WhatIf: nothing was created." -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------- resource group
Write-Step "Resource group"
if (Test-AzResource group show --name $resourceGroup) {
    Write-Ok "$resourceGroup already exists - reusing"
} else {
    Invoke-Az group create --name $resourceGroup --location $Location `
        --tags app=$Name managed-by=provision-azure.ps1 | Out-Null
    Write-Ok "created $resourceGroup"
}

# --------------------------------------------------------------- app service
Write-Step "App Service (free F1)"
if (Test-AzResource appservice plan show --name $planName --resource-group $resourceGroup) {
    Write-Ok "plan $planName already exists"
} else {
    Invoke-Az appservice plan create --name $planName --resource-group $resourceGroup `
        --location $Location --sku F1 --is-linux | Out-Null
    Write-Ok "created plan $planName (F1, Linux)"
}

if (Test-AzResource webapp show --name $apiName --resource-group $resourceGroup) {
    Write-Ok "web app $apiName already exists"
} else {
    Invoke-Az webapp create --name $apiName --resource-group $resourceGroup `
        --plan $planName --runtime "DOTNETCORE:10.0" | Out-Null
    Write-Ok "created web app $apiName"
}

$apiUrl = "https://$apiName.azurewebsites.net"

# System-assigned managed identity: the app's own Azure identity, so it can
# reach Azure SQL (and anything else) with no password in existence.
Invoke-Az webapp identity assign --name $apiName --resource-group $resourceGroup | Out-Null
Write-Ok "managed identity assigned"

# ------------------------------------------------------------------ database
Write-Step "Database ($Database)"
if ($Database -eq "Sqlite") {
    # /home is the App Service persistent volume - survives restarts and
    # deployments. The database is fully reconstructible from public sources
    # anyway (see the README), so this costs nothing and risks nothing.
    Invoke-Az webapp config appsettings set --name $apiName --resource-group $resourceGroup `
        --settings "Database__Provider=Sqlite" "ConnectionStrings__Default=Data Source=/home/data/lottery.db" | Out-Null
    Write-Ok "SQLite on the persistent /home volume - `$0"
} else {
    $sqlServer = "sql-$Name-$suffix"
    $sqlDb     = "sqldb-$Name"

    if (-not (Test-AzResource sql server show --name $sqlServer --resource-group $resourceGroup)) {
        # Entra-only authentication: no SQL admin password is ever created,
        # so there is no credential to leak, rotate, or store.
        Invoke-Az sql server create --name $sqlServer --resource-group $resourceGroup `
            --location $Location --enable-ad-only-auth --external-admin-principal-type User `
            --external-admin-name $account.user.name `
            --external-admin-sid (Invoke-Az ad signed-in-user show --query id) | Out-Null
        Write-Ok "created SQL server $sqlServer (Entra-only auth, no password)"
    } else {
        Write-Ok "SQL server $sqlServer already exists"
    }

    if (-not (Test-AzResource sql db show --name $sqlDb --server $sqlServer --resource-group $resourceGroup)) {
        Write-Info "requesting the free serverless offer (one per subscription)..."
        try {
            Invoke-Az sql db create --name $sqlDb --server $sqlServer --resource-group $resourceGroup `
                --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 1 `
                --use-free-limit --free-limit-exhaustion-behavior AutoPause | Out-Null
            Write-Ok "created $sqlDb on the free offer"
        } catch {
            throw "Could not create the free-tier database. The free offer allows one per subscription and may already be in use. Re-run with -Database Sqlite to stay at `$0.`n$_"
        }
    } else {
        Write-Ok "database $sqlDb already exists"
    }

    Invoke-Az sql server firewall-rule create --name AllowAzureServices `
        --server $sqlServer --resource-group $resourceGroup `
        --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 2>$null | Out-Null

    $connection = "Server=tcp:$sqlServer.database.windows.net,1433;Database=$sqlDb;Authentication=Active Directory Default;Encrypt=True;"
    Invoke-Az webapp config appsettings set --name $apiName --resource-group $resourceGroup `
        --settings "Database__Provider=SqlServer" "ConnectionStrings__Default=$connection" | Out-Null
    Write-Ok "connection string set (Managed Identity - contains no password)"
}

# ------------------------------------------------------------ static web app
Write-Step "Static Web App (free)"
if (Test-AzResource staticwebapp show --name $swaName --resource-group $resourceGroup) {
    Write-Ok "$swaName already exists"
} else {
    Invoke-Az staticwebapp create --name $swaName --resource-group $resourceGroup `
        --location $Location --sku Free | Out-Null
    Write-Ok "created $swaName (Free)"
}

$swaHost = (Invoke-Az staticwebapp show --name $swaName --resource-group $resourceGroup --query defaultHostname)
$swaUrl  = "https://$swaHost"

# The Free SKU does NOT support linked backends (that is a Standard feature),
# so the browser calls the API's own origin directly and the API must allow it.
Write-Step "CORS"
Invoke-Az webapp cors add --name $apiName --resource-group $resourceGroup --allowed-origins $swaUrl | Out-Null
Invoke-Az webapp config appsettings set --name $apiName --resource-group $resourceGroup `
    --settings "Cors__AllowedOrigins__0=$swaUrl" | Out-Null
Write-Ok "API allows $swaUrl"

# ------------------------------------------------------- GitHub OIDC identity
Write-Step "GitHub OIDC federation (no stored credential)"

$appId = Invoke-Az ad app list --display-name $identityName --query "[0].appId"
if (-not $appId) {
    $appId = Invoke-Az ad app create --display-name $identityName --query appId
    Write-Ok "created app registration $identityName"
} else {
    Write-Ok "app registration $identityName already exists"
}

if (-not (Invoke-Az ad sp list --filter "appId eq '$appId'" --query "[0].id")) {
    Invoke-Az ad sp create --id $appId | Out-Null
    Write-Ok "created service principal"
}

# One federated credential per trusted GitHub context. GitHub presents a
# short-lived token proving "I am this workflow on this ref" - nothing is stored.
$subjects = @{
    "main"         = "repo:${repoSlug}:ref:refs/heads/main"
    "frontend"     = "repo:${repoSlug}:ref:refs/heads/frontend"
    "pull-request" = "repo:${repoSlug}:pull_request"
}
$existingCreds = (Invoke-Az ad app federated-credential list --id $appId) | ForEach-Object { $_.name }
foreach ($cred in $subjects.GetEnumerator()) {
    if ($existingCreds -contains $cred.Key) {
        Write-Ok "federated credential '$($cred.Key)' already exists"
        continue
    }
    $body = @{
        name      = $cred.Key
        issuer    = "https://token.actions.githubusercontent.com"
        subject   = $cred.Value
        audiences = @("api://AzureADTokenExchange")
    } | ConvertTo-Json -Compress

    $temp = New-TemporaryFile
    try {
        # Written to the OS temp dir, never the repo, and deleted immediately.
        Set-Content -Path $temp -Value $body -Encoding utf8
        Invoke-Az ad app federated-credential create --id $appId --parameters "@$temp" | Out-Null
        Write-Ok "added federated credential '$($cred.Key)'"
    } finally {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
    }
}

$spId = Invoke-Az ad sp list --filter "appId eq '$appId'" --query "[0].id"
$scope = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup"
if (-not (Invoke-Az role assignment list --assignee $appId --scope $scope --query "[?roleDefinitionName=='Contributor'] | [0].id")) {
    Invoke-Az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal `
        --role Contributor --scope $scope | Out-Null
    Write-Ok "granted Contributor on $resourceGroup (scoped - not subscription-wide)"
} else {
    Write-Ok "role assignment already in place"
}

# --------------------------------------------------------- GitHub wiring
Write-Step "GitHub repository configuration"

# Non-sensitive identifiers -> repository VARIABLES (visible, not secret).
$variables = @{
    AZURE_CLIENT_ID       = $appId
    AZURE_TENANT_ID       = $tenantId
    AZURE_SUBSCRIPTION_ID = $subscriptionId
    AZURE_RESOURCE_GROUP  = $resourceGroup
    API_APP_NAME          = $apiName
    API_BASE_URL          = $apiUrl
    WEB_BASE_URL          = $swaUrl
}
foreach ($v in $variables.GetEnumerator()) {
    & gh variable set $v.Key --repo $repoSlug --body $v.Value 2>&1 | Out-Null
    Write-Ok "variable $($v.Key)"
}

# The SWA deployment token is a genuine secret: piped straight from az into
# gh, never written to disk and never printed.
$swaToken = Invoke-Az staticwebapp secrets list --name $swaName --resource-group $resourceGroup `
    --query "properties.apiKey"
$swaToken | & gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --repo $repoSlug 2>&1 | Out-Null
Remove-Variable swaToken -ErrorAction SilentlyContinue
Write-Ok "secret AZURE_STATIC_WEB_APPS_API_TOKEN (piped, never stored locally)"

# ------------------------------------------------------------------- summary
Write-Step "Done"
Write-Host @"

  API   $apiUrl
  Web   $swaUrl
  Group $resourceGroup  (delete everything: az group delete --name $resourceGroup)

  Cost: `$0 - F1 App Service, Free Static Web App$(if ($Database -eq 'Sqlite') { ', SQLite on /home' } else { ', Azure SQL free offer' })

  Next:
    1. Uncomment the push triggers in .github/workflows/deploy-api.yml (main)
       and .github/workflows/deploy-web.yml (frontend).
    2. Push, or run those workflows manually, to deploy.
    3. The keep-alive workflow starts pinging automatically now that
       API_BASE_URL is set (F1 has no Always On).

  No secret was written to disk by this script.

"@ -ForegroundColor White
