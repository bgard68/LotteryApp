<#
.SYNOPSIS
    Query the SQLite database on the deployed App Service.
.DESCRIPTION
    The production database is a file (/home/data/lottery.db) inside the app's
    container, not a managed service - so there is no connection string to point
    a client at. This wraps the Kudu command API, which runs sqlite3 in place.

    Auth is Entra-only: SCM basic authentication is disabled on the app, so
    there is no publish-profile password to leak. Your own RBAC on the site is
    what grants access; `az login` is the only prerequisite.

    Writes are refused unless -AllowWrite is passed. The app holds this file
    open, and there is no backup by design (the data is re-seedable from the
    committed snapshots), so an accidental UPDATE is the one expensive mistake
    available here.
.EXAMPLE
    .\query-db.ps1 "SELECT * FROM ImportLedger;"
.EXAMPLE
    .\query-db.ps1 "SELECT Game, COUNT(*) FROM Draws GROUP BY Game;"
.EXAMPLE
    .\query-db.ps1 -Download .\lottery-live.db
    # Pull a copy down for a GUI (DB Browser for SQLite) or heavy analysis.
#>
[CmdletBinding(DefaultParameterSetName = 'Query')]
param(
    [Parameter(ParameterSetName = 'Query', Position = 0, Mandatory)]
    [string]$Query,

    # Save a copy of the live database locally instead of querying in place.
    [Parameter(ParameterSetName = 'Download', Mandatory)]
    [string]$Download,

    [string]$AppName = "app-lottery-8e49d22b",
    [string]$ResourceGroup = "rg-lottery",
    [string]$DbPath = "/home/data/lottery.db",

    # Required for anything that is not a read. Deliberately awkward.
    [switch]$AllowWrite
)

$ErrorActionPreference = "Stop"

$scm = "https://$AppName.scm.azurewebsites.net"

Write-Host "Acquiring token..." -ForegroundColor DarkGray
$token = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host "Could not get a token. Run 'az login' first." -ForegroundColor Red
    exit 1
}
$headers = @{ Authorization = "Bearer $token" }

if ($PSCmdlet.ParameterSetName -eq 'Download') {
    # VFS is rooted at /home, so the path here omits it.
    $vfs = "$scm/api/vfs" + ($DbPath -replace '^/home', '')
    Write-Host "Downloading $DbPath -> $Download" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $vfs -Headers $headers -OutFile $Download -UseBasicParsing
    $size = (Get-Item $Download).Length
    Write-Host ("Saved {0:N0} bytes. Open it with any SQLite client - it is a copy, so nothing you do affects production." -f $size) -ForegroundColor Green
    exit 0
}

# A read-only default is worth the false positives: the app has this file open
# and there is no backup to restore from.
$writeVerbs = 'INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|TRUNCATE|VACUUM|ATTACH|PRAGMA\s+\w+\s*='
if (-not $AllowWrite -and $Query -match "(?i)\b($writeVerbs)\b") {
    Write-Host "Refused: that looks like a write, and the app holds this file open." -ForegroundColor Red
    Write-Host "There is no backup - the data is re-seedable from committed snapshots, not restorable." -ForegroundColor Yellow
    Write-Host "If you are sure, re-run with -AllowWrite. To experiment safely, use -Download instead." -ForegroundColor Yellow
    exit 1
}

# Escape for JSON, then for the shell's double quotes around the SQL.
$sql = $Query -replace '\\', '\\' -replace '"', '\"'
$command = "sqlite3 -header -column $DbPath `"$sql`""
$body = @{ command = $command; dir = "/home" } | ConvertTo-Json -Compress

try {
    $response = Invoke-RestMethod -Uri "$scm/api/command" -Method Post -Headers $headers `
        -ContentType "application/json" -Body $body -TimeoutSec 120
}
catch {
    Write-Host "Request failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "If this is 401/403, check your role on the site: az role assignment list --assignee <you>" -ForegroundColor Yellow
    exit 1
}

if ($response.Output) { Write-Host $response.Output.TrimEnd() }

# sqlite3 reports SQL errors on stderr with a non-zero exit; surface both.
if ($response.Error) {
    Write-Host $response.Error.TrimEnd() -ForegroundColor Red
    exit 1
}
if (-not $response.Output) { Write-Host "(no rows)" -ForegroundColor DarkGray }
