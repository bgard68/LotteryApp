# Scripts

Back to the [main README](../../README.md).

## smoke-test.ps1

End-to-end verification of a **running** API - 22 checks covering every
endpoint, both games, happy paths **and error conditions**:

- Health first (`/healthz`) - fail fast if the stack is down.
- Happy paths: next-draw, latest, draws (with date filters), rule-eras,
  generate, and ticket checks for both games.
- Error conditions: unknown game (404), missing/short/duplicate/out-of-era/
  non-numeric ticket parameters (400 with the expected reason in the body).

```powershell
# local dev server
.\scripts\smoke-test.ps1 -BaseUrl http://localhost:5000

# deployed instance
.\scripts\smoke-test.ps1 -BaseUrl https://<app>.azurewebsites.net
```

Exit code 0 = all green; non-zero = failure (with per-check output), which is
what makes it usable as the **post-deploy gate** in the Phase 4 deployment
workflow - deploy, smoke-test the live URL, and a red check fails the run.

Compatibility note: the script reads HTTP error bodies via `ErrorDetails`
(PowerShell 7+) with a response-stream fallback for Windows PowerShell 5.1 -
see [lessons learned](../docs/LESSONS-LEARNED.md#4-windows-powershell-51-hides-http-error-bodies).
