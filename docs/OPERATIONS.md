# Operations runbook

Things you do to the *running* system rather than to the code. Back to the
[main README](../README.md); architecture is in [ARCHITECTURE.md](ARCHITECTURE.md),
provisioning in [AZURE-DEPLOYMENT.md](AZURE-DEPLOYMENT.md).

## Querying the production database

The production database is a **file inside the app's container**
(`/home/data/lottery.db`), not a managed service - so there is no connection
string to point a client at, no firewall rule to add, and no server to browse.
Everything below reaches it through the App Service SCM (Kudu) endpoint.

**Authentication is Entra-only.** SCM basic authentication is disabled on this
app, so no publish-profile password exists to leak. Access is granted by your
own RBAC on the site; `az login` is the only prerequisite. If a call returns
401 or 403, that is a role problem, not a credential problem.

### The script

```powershell
./scripts/query-db.ps1 "SELECT Game, COUNT(*) FROM Draws GROUP BY Game;"
```

```
Game          Draws  Latest
------------  -----  ----------
MegaMillions  2523   2026-07-28
Powerball     1972   2026-07-27
```

Pull a copy down for a GUI or heavy analysis:

```powershell
./scripts/query-db.ps1 -Download .\lottery-live.db
```

393 KB. Open it in [DB Browser for SQLite](https://sqlitebrowser.org/) or any
client - it is a copy, so nothing you do to it touches production. **This is the
right choice for anything exploratory.**

**Writes are refused unless you pass `-AllowWrite`.** That is deliberate, and
the reason is worth stating plainly: the app holds this file open, and there is
no backup. The data is *re-seedable* (committed snapshots plus a Socrata
re-fetch, idempotent via the `ImportLedger`) - which is not the same as
restorable. Re-seeding gets you the published history back; it does not get back
anything that only existed in production.

### Without the script

An interactive shell, best for exploring:

```bash
az webapp ssh --name app-lottery-8e49d22b --resource-group rg-lottery
# then:
sqlite3 /home/data/lottery.db
```

`sqlite3` is present in the container - worth knowing, because the .NET runtime
image is not obliged to include it and a future base-image change could remove
it. If it ever disappears, the `-Download` path still works.

One call, no session:

```bash
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)

curl -s -X POST \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"command":"sqlite3 -header -column /home/data/lottery.db \"SELECT * FROM ImportLedger;\"","dir":"/home"}' \
  https://app-lottery-8e49d22b.scm.azurewebsites.net/api/command
```

Returns JSON with `Output`, `Error` and `ExitCode`.

### The schema

| Table | Rows | What it is |
|---|---|---|
| `Draws` | 1,972 Powerball + 2,523 Mega Millions | Every stored drawing |
| `ImportLedger` | 1 per game | The guard that makes re-seeding a no-op |
| `JackpotEstimates` | 1 per game | Latest estimate, written by the refresh cycle |
| `SchemaVersions` | 1 per migration | DbUp's record |

Two things that will waste your time if nobody tells you:

- **`Game` is stored as text**, not an ordinal. `WHERE Game = 0` returns nothing,
  silently. Use `WHERE Game = 'Powerball'`.
- **The Kudu VFS path is already rooted at `/home`**, so the database is at
  `/api/vfs/data/lottery.db` - `/api/vfs/home/data/...` gives a confusing 404
  that reads as "the file is missing".

### Useful queries

```sql
-- Has the seed run, and what did it cover?
SELECT * FROM ImportLedger;

-- Any gaps in the recent record?
SELECT DrawDate FROM Draws WHERE Game='Powerball' ORDER BY DrawDate DESC LIMIT 10;

-- What the cards are showing
SELECT * FROM JackpotEstimates;

-- Did a drawing land but arrive without numbers? (should return nothing)
SELECT * FROM Draws WHERE White1 IS NULL;

-- Duplicate guard: the unique index should make this impossible
SELECT Game, DrawDate, COUNT(*) FROM Draws GROUP BY Game, DrawDate HAVING COUNT(*) > 1;
```

## Reading the logs

Diagnostics were off until the 2026-07-28 review ([D3](SECURITY-POSTURE.md)).
They are now on:

```bash
az webapp log tail --name app-lottery-8e49d22b --resource-group rg-lottery
```

**Filesystem logging auto-disables after 12 hours.** It is the right tool for
"something is wrong now"; a durable trail needs a storage-account sink, which is
the next step if it ever matters more than it does today.

## Forcing a data refresh

The background service wakes shortly after each drawing, and startup runs a
gap-repair pass - so downtime self-heals without intervention. To trigger the
same cycle on demand:

```bash
curl -X POST -H "X-Refresh-Key: <REFRESH_KEY>" \
  https://app-lottery-8e49d22b.azurewebsites.net/internal/refresh
```

The key lives in the `REFRESH_KEY` GitHub secret and the `Refresh__Key` app
setting - the two are set together and must match. An unkeyed call returns 401,
and the smoke test asserts both directions.

## Rolling back

There are no deployment slots on the F1 tier, so there is no swap-back. Rolling
back means reverting the commit and letting the deploy run - a few minutes,
gated by the smoke test. Recorded as an accepted limitation in
[D9](SECURITY-POSTURE.md).
