# Lottery.Application

Use cases and ports. This layer depends only on Domain; it defines the
interfaces (**ports**) that Infrastructure implements - the Dependency
Inversion seam of the whole system.

Back to the [main README](../../README.md).

## Ports (`Abstractions/`)

| Port | Implemented by | Purpose |
|---|---|---|
| `IDrawRepository` | Dapper repository | All draw reads/writes; each method is a deliberate named query |
| `IImportLedger` | Dapper repository | Records completed one-time imports so they never re-run |
| `IHistorySource` | Snapshot reader (Phase 2 adds the live feed) | Where historical numbers come from |
| `IDatabaseInitializer` | DbUp runner | Schema migrations at startup |
| `IWinningNumbersFeed` | Live Socrata client | New draws after a date (refresh + gap-repair) |
| `IJackpotFeed` | Composite of per-game adapters | Jackpot info; everything nullable by design |
| `IJackpotStore` | Dapper repository | Persisted latest estimate per game |

## Use cases (`UseCases/`)

Plain injected classes - **no MediatR** (see the main README for the
reasoning). One class per user-facing operation:

- `GetNextDraw` - schedule math + `TimeProvider`
- `GetLatestDraw` - returns `Pending` when the schedule says a drawing
  happened but no numbers are stored yet
- `GetDraws` - history with capped limits
- `CheckTicket` - validates picks against the **current** era (400-style
  rejection with a reason), then matches against all history; distinguishes
  `DataUnavailable` (no history imported) from "checked everything, no wins"
- `GeneratePicks` - era-valid random picks via `IPickGenerator`
- `GetRuleEras` - the era table for clients (pickers, validation ranges)
- `ImportHistory` - the one-time seed: ledger check -> validate every draw
  against its era -> bulk insert -> write ledger. Any violation aborts before
  anything is written
- `RefreshGame` - one refresh cycle: gap-repair new draws from the live feed
  (era-validating each; invalid rows are skipped and counted), then refresh
  jackpot data. Feed failures are reported in the result, never thrown

`TimeProvider` is injected everywhere time matters, so tests drive these
classes with `FakeTimeProvider` and virtual time.
