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
    [string]$BaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"
$failures = @()
$passes = 0

function Invoke-Api {
    param([string]$Path, [string]$Method = "GET")
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl$Path" -Method $Method -UseBasicParsing -TimeoutSec 90
        return @{ Status = [int]$response.StatusCode; Body = $response.Content }
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
        return @{ Status = $status; Body = $body }
    }
}

function Assert-Api {
    param([string]$Name, [string]$Path, [int]$ExpectedStatus, [string]$BodyContains = $null, [string]$Method = "GET")
    $result = Invoke-Api -Path $Path -Method $Method
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

# --- Health first: fail fast if the stack is down ---
Assert-Api "healthz" "/healthz" 200 "Healthy"

# --- Happy paths, both games ---
foreach ($game in @("powerball", "megamillions")) {
    Assert-Api "$game next-draw"  "/api/$game/next-draw"  200 "drawTimeUtc"
    Assert-Api "$game latest"     "/api/$game/latest"     200 "drawDate"
    Assert-Api "$game draws"      "/api/$game/draws?limit=5" 200 "whiteBalls"
    Assert-Api "$game rule-eras"  "/api/$game/rule-eras"  200 "whiteBallMax"
    Assert-Api "$game generate"   "/api/$game/generate"   200 "whiteBalls"
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

# --- Refresh trigger (Phase 2) - always 200; feed failures are reported in-body ---
Assert-Api "internal refresh" "/internal/refresh" 200 "upToDate" -Method POST

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "SMOKE TEST FAILED: $($failures.Count) failure(s), $passes passed." -ForegroundColor Red
    exit 1
}
Write-Host "SMOKE TEST PASSED: all $passes checks green." -ForegroundColor Green
exit 0
