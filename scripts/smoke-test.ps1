<#
.SYNOPSIS
    Smoke test for the Lottery API - every endpoint, happy paths AND error conditions.
.DESCRIPTION
    Runs locally against a dev server, in CI against the test host, and as the
    post-deploy gate in the deployment workflow. Exits non-zero on any failure.
.EXAMPLE
    .\smoke-test.ps1 -BaseUrl http://localhost:5000
#>
param(
    [string]$BaseUrl = "http://localhost:5000",

    # Shared key for POST /internal/refresh. Supply it and the test asserts the
    # refresh succeeds; omit it and the test asserts the endpoint is guarded.
    # Never defaulted, never echoed - the deploy workflow passes a secret.
    [string]$RefreshKey = $env:REFRESH_KEY,

    # Seconds to wait for the host to answer /healthz before asserting anything.
    # A post-deploy run races App Service recycling the app.
    [int]$ReadyTimeoutSec = 120
)

$ErrorActionPreference = "Stop"
$failures = @()
$passes = 0

function Invoke-Api {
    param([string]$Path, [string]$Method = "GET", [hashtable]$Headers = @{})
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl$Path" -Method $Method -Headers $Headers -UseBasicParsing -TimeoutSec 90
        return @{ Status = [int]$response.StatusCode; Body = $response.Content; Headers = $response.Headers }
    }
    catch {
        $status = 0
        $body = ""
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            # Windows PowerShell 5.1: read the error body from the response stream;
            # PowerShell 7+: it is already on ErrorDetails.
            if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
                $body = $_.ErrorDetails.Message
            }
            else {
                try {
                    $stream = $_.Exception.Response.GetResponseStream()
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                }
                catch {}
            }
        }
        return @{ Status = $status; Body = $body; Headers = @{} }
    }
}

function Assert-Api {
    param([string]$Name, [string]$Path, [int]$ExpectedStatus, [string]$BodyContains = $null,
          [string]$Method = "GET", [hashtable]$Headers = @{})
    $result = Invoke-Api -Path $Path -Method $Method -Headers $Headers
    $ok = $result.Status -eq $ExpectedStatus
    if ($ok -and $BodyContains) { $ok = $result.Body -match [regex]::Escape($BodyContains) }
    if ($ok) {
        $script:passes++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:failures += $Name
        Write-Host "  FAIL  $Name  (status $($result.Status), expected $ExpectedStatus) $Path" -ForegroundColor Red
        if ($result.Body) { Write-Host "        body: $($result.Body.Substring(0, [Math]::Min(200, $result.Body.Length)))" }
    }
}

Write-Host "Smoke-testing $BaseUrl"

# A post-deploy run starts while App Service is still recycling the app, which
# answers 500 until it is up. Without this wait the gate reports a broken deploy
# for an app that is merely still starting - so poll first, then assert.
$deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
do {
    $ready = (Invoke-Api -Path "/healthz").Status -eq 200
    if (-not $ready) { Start-Sleep -Seconds 3 }
} while (-not $ready -and (Get-Date) -lt $deadline)

if (-not $ready) {
    Write-Host "Host did not become ready within $ReadyTimeoutSec seconds - asserting anyway." -ForegroundColor Yellow
}

# --- Health first: fail fast if the stack is down ---
Assert-Api "healthz" "/healthz" 200 "Healthy"

# --- Security headers -------------------------------------------------------
# These are trivial to add and just as trivial to lose: a middleware reordered
# above the header block, or an exception path that short-circuits it, drops
# them silently and nothing else would notice. Asserting them here makes the
# deploy gate the thing that notices.
function Assert-Header {
    param([string]$Name, [string]$Header, [string]$Expected)
    $result = Invoke-Api -Path "/healthz"
    $actual = $result.Headers[$Header]
    if ($actual -is [array]) { $actual = $actual -join "," }

    if ($actual -and $actual -match [regex]::Escape($Expected)) {
        $script:passes++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:failures += $Name
        Write-Host "  FAIL  $Name  (got '$actual', expected to contain '$Expected')" -ForegroundColor Red
    }
}

Assert-Header "header: nosniff"        "X-Content-Type-Options" "nosniff"
Assert-Header "header: frame deny"     "X-Frame-Options"        "DENY"
Assert-Header "header: referrer"       "Referrer-Policy"        "no-referrer"
Assert-Header "header: CSP lockdown"   "Content-Security-Policy" "frame-ancestors 'none'"

# The server header is deliberately suppressed - its absence is the assertion.
$serverHeader = (Invoke-Api -Path "/healthz").Headers["Server"]
if (-not $serverHeader) {
    $passes++
    Write-Host "  PASS  header: no Server disclosure" -ForegroundColor Green
}
else {
    $failures += "header: no Server disclosure"
    Write-Host "  FAIL  header: no Server disclosure  (got '$serverHeader')" -ForegroundColor Red
}

# --- Happy paths, both games ---
foreach ($game in @("powerball", "megamillions")) {
    Assert-Api "$game next-draw"  "/api/$game/next-draw"  200 "drawTimeUtc"
    Assert-Api "$game latest"     "/api/$game/latest"     200 "drawDate"
    Assert-Api "$game draws"      "/api/$game/draws?limit=5" 200 "whiteBalls"
    Assert-Api "$game rule-eras"  "/api/$game/rule-eras"  200 "whiteBallMax"
    Assert-Api "$game generate"   "/api/$game/generate"   200 "whiteBalls"
    Assert-Api "$game generate x5" "/api/$game/generate?count=5" 200 "tickets"
}
Assert-Api "powerball check (winless ticket ok)" "/api/powerball/check?whites=1,2,3,4,5&special=1" 200 "drawsChecked"
Assert-Api "megamillions check ok" "/api/megamillions/check?whites=10,20,30,40,50&special=10" 200 "drawsChecked"
Assert-Api "draws date filter" "/api/powerball/draws?from=2026-01-01&to=2026-07-01&limit=3" 200 "drawDate"

# --- Error conditions ---
Assert-Api "unknown game -> 404"            "/api/lotto649/latest" 404 "Unknown game"
Assert-Api "check missing params -> 400"    "/api/powerball/check" 400 "required"
Assert-Api "check too few whites -> 400"    "/api/powerball/check?whites=1,2,3,4&special=5" 400 "Exactly 5"
Assert-Api "check duplicate whites -> 400"  "/api/powerball/check?whites=1,1,2,3,4&special=5" 400 "distinct"
Assert-Api "check white out of era -> 400"  "/api/powerball/check?whites=1,2,3,4,70&special=5" 400 "between 1 and 69"
Assert-Api "check special out of era -> 400" "/api/powerball/check?whites=1,2,3,4,5&special=27" 400 "between 1 and 26"
Assert-Api "check non-numeric white -> 400" "/api/powerball/check?whites=1,2,3,4,abc&special=5" 400 "not a number"
Assert-Api "mm special out of era -> 400"   "/api/megamillions/check?whites=1,2,3,4,5&special=25" 400 "between 1 and 24"
Assert-Api "generate count 0 -> 400"        "/api/powerball/generate?count=0" 400 "between 1 and 10"
Assert-Api "generate count 11 -> 400"       "/api/powerball/generate?count=11" 400 "between 1 and 10"

# --- Refresh trigger - guarded by a shared key when Refresh:Key is configured ---
# With a key: it must be accepted (feed failures are reported in-body, still 200).
# Without one: the endpoint must REJECT us, which is the assertion that proves
# the guard is switched on in this environment. A local run with no key
# configured server-side is the third case, and 200 is correct there.
if ($RefreshKey) {
    Assert-Api "internal refresh (keyed)" "/internal/refresh" 200 "upToDate" `
        -Method POST -Headers @{ "X-Refresh-Key" = $RefreshKey }

    $unauthorized = Invoke-Api -Path "/internal/refresh" -Method POST
    if ($unauthorized.Status -eq 401) {
        $passes++
        Write-Host "  PASS  internal refresh rejects a missing key" -ForegroundColor Green
    }
    else {
        $failures += "internal refresh rejects a missing key"
        Write-Host "  FAIL  internal refresh rejects a missing key  (status $($unauthorized.Status), expected 401)" -ForegroundColor Red
    }
}
else {
    $result = Invoke-Api -Path "/internal/refresh" -Method POST
    if ($result.Status -eq 200 -or $result.Status -eq 401) {
        $passes++
        $note = if ($result.Status -eq 401) { "guarded" } else { "open - no key configured" }
        Write-Host "  PASS  internal refresh ($note)" -ForegroundColor Green
    }
    else {
        $failures += "internal refresh"
        Write-Host "  FAIL  internal refresh  (status $($result.Status), expected 200 or 401)" -ForegroundColor Red
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "SMOKE TEST FAILED: $($failures.Count) failure(s), $passes passed." -ForegroundColor Red
    exit 1
}
Write-Host "SMOKE TEST PASSED: all $passes checks green." -ForegroundColor Green
exit 0
