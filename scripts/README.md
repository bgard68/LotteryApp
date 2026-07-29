# Scripts

Back to the [main README](../../README.md).

## smoke-test.ps1

End-to-end verification of a **running** API - 32 checks covering every
endpoint, both games, happy paths **and error conditions**:

- Health first (`/healthz`) - fail fast if the stack is down.
- Happy paths: next-draw, latest, draws (with date filters), rule-eras,
  generate, and ticket checks for both games.
- Error conditions: unknown game (404), missing/short/duplicate/out-of-era/
  non-numeric ticket parameters (400 with the expected reason in the body).
- `POST /internal/refresh` - always 200 (feed failures are reported in the
  response body, not as HTTP errors), so this check stays deterministic even
  when external feeds are down.

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

## query-db.ps1

Queries the SQLite database on the deployed App Service. The production database
is a file inside the container, so there is no connection string a client could
use - this runs `sqlite3` in place through the Kudu command API, authenticated
with your own Entra token (SCM basic auth is disabled; there is no password).

```powershell
./query-db.ps1 "SELECT * FROM ImportLedger;"   # read
./query-db.ps1 -Download .\live.db            # copy for a GUI
```

Writes are refused unless `-AllowWrite` is passed: the app holds the file open
and there is no backup - the data is re-seedable, which is not the same as
restorable. Full runbook: [docs/OPERATIONS.md](../docs/OPERATIONS.md).
