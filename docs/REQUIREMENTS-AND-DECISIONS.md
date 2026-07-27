# Requirements, decisions, and approved design

The project's requirements, the decision log that shaped the architecture, and
the final approved design. This is the "why is it built this way" record; the
"how it's built" lives in the [main README](../README.md).

## Requirements

1. Generate realistic Powerball and Mega Millions picks.
2. Show the next draw date with a live countdown.
3. Show the last winning numbers with date - red special ball for Powerball,
   yellow for Mega Millions.
4. Show the last drawing's jackpot amount and the upcoming estimated jackpot.
5. Seed a database with past winning numbers, one dataset per game.
6. Let users check their numbers against all past winning numbers.
7. Pull full history **exactly once**; afterwards add each new drawing
   incrementally.
8. Handle time through an injectable provider with proper UTC handling; the
   live feed is the only wall-clock exception.
9. Lottery rules changed over time (e.g. Powerball's number ranges) - the
   system must detect and handle rule eras, verified by tests.
10. Clean Architecture, SOLID, DRY, DIP, SRP - **frontend and backend both**.
11. No sensitive information in any committed file - secrets from environment,
    Key Vault, etc.; docs may show placeholder instructions only.
12. Frontend and backend deploy **independently** - a backend-only change must
    not require a frontend deploy, and vice versa.

## Decision log

Each decision as discussed and settled, with the deciding rationale.

| # | Decision | Rationale |
|---|---|---|
| D1 | **.NET 10 LTS + C# 14**, minimal APIs, built-in OpenAPI | Current LTS (support to Nov 2028); one less dependency than Swashbuckle |
| D2 | **No EF Core -> Dapper + DbUp** | Read-heavy stable schema, no object graph; with a micro-ORM each repository method is a deliberate named query. DbUp replaces EF migrations with plain SQL |
| D3 | **SQLite dev / Azure SQL serverless prod**, config-switched factories | Zero-install local dev; serverless costs near nothing idle. Dialect divergences isolated to the factory + a few per-dialect statements |
| D4 | **Connectionless DB access** | Connection-per-operation, disposed immediately, pooled by the driver; SQL Server factory retries Azure serverless auto-pause wake-ups |
| D5 | **`TimeProvider`, not a custom `IDateTimeProvider`** | Same abstraction, framework-blessed, plus timer/delay support and the official `FakeTimeProvider` - the background service's sleep-until-draw-time is testable in virtual time |
| D6 | **No MediatR** | Seven use cases, one consumer each; plain injected classes are clearer, and MediatR moved to commercial licensing. Recorded so it isn't added by habit |
| D7 | **History = NY Open Data (Socrata)**; **draw dates + jackpots = powerball.com / megamillions.com JSON**; **never scrape HTML** | Socrata is the only stable full-history source; official-site JSON carries jackpots the Socrata sets lack. Schedule math is the fallback for dates; jackpot amounts hide gracefully if endpoints change |
| D8 | **Seed from committed JSON snapshots** (then live feed for gap-repair) | First boot is offline and deterministic; tests validate the full real history on every CI run |
| D9 | **One-time import guarded by an `ImportLedger`** | Requirement 7 made mechanical: ledger row per game, unique `(Game, DrawDate)` index makes every retry idempotent |
| D10 | **Rule eras as reference data + validator + tests** | Era table (7 Powerball eras to 1992, 5 Mega Millions) validated against all 4,493 real draws; an undocumented rule change fails loudly. Weekly CI run planned to catch future changes within days |
| D11 | **`GET /check`** (not POST) | Reads data, changes nothing - cacheable and smoke-testable with a plain URL |
| D12 | **`Pending` draw status** | A drawing that happened but has no published numbers yet is shown as pending - never silently presenting the previous draw as newest, and never treating missing data as "you lost" |
| D13 | **Prize tiers: stable names, approximate amounts, honesty disclaimer** | Tier names are stable across history; dollar amounts are current-era approximations (Mega Millions post-2025 amounts are pre-multiplier); historical "wins" are labeled unclaimable |
| D14 | **Zero committed secrets** | SQLite needs none; Azure uses Managed Identity (no SQL password exists) + Key Vault; deploys use GitHub OIDC (no stored deploy credential). Placeholder-only docs; scripts discover values dynamically (env vars -> az/gh CLI -> tags) and never echo them |
| D15 | **Hosting: Azure Static Web Apps (frontend) + App Service (API), linked** | Requirement 12: two hosts with **path-filtered workflows** deploy independently; the SWA "linked backend" proxies `/api/*` so the browser sees one origin and CORS never applies |
| D16 | **Keep-alive via scheduled GitHub Actions, not Always On** | Free cron pings `/healthz` around draw times; gap-repair makes missed fetches self-healing either way |
| D17 | **PowerShell smoke test as a deploy gate** | Every endpoint including error conditions; runs locally, in CI, and against the live URL after deploy - a red check fails the deployment |
| D18 | **API-first build order, phased** | Phase 1 backend + tests + smoke test; Phase 2 live feeds; Phase 3 Angular; Phase 4 Azure + workflows. Each phase ships something runnable |
| D19 | **Repo history order: empty init -> security -> line endings/ignore -> license -> code** | Dependabot + CodeQL + secret scanning + push protection active before any code landed; `.gitattributes` first so every file entered normalized |
| D20 | **Angular mirrors the onion** | `core/domain` (pure TS) -> ports (`LotteryApi`, `CLOCK` injection tokens) -> adapters -> signal stores -> dumb components; DTO types generated from the OpenAPI document with a CI drift check |

## Considered and rejected

- **EF Core / DbContext** - see D2.
- **MediatR** - see D6.
- **HTML scraping for results** - brittle by design; rejected outright.
- **API versioning, Redis/output caching, Docker for local dev, auth system** -
  complexity without a driver at this scale; revisit only if requirements change.
- **Single-host deployment** (frontend served from API `wwwroot`) - simpler,
  but rejected because it violates requirement 12 (independent deploys).
- **Always On for the App Service** - paid feature replaced by D16.

## Final approved design

Approved 2026-07-27; Phase 1 built to this spec and verified (49 tests, 22
smoke checks against a live run):

- .NET 10 Clean Architecture backend: `Domain <- Application <- Infrastructure <- Api`,
  ports owned by Application, composition root in `Program.cs`.
- Dapper + DbUp over SQLite (dev) / Azure SQL serverless (prod), connectionless
  access, five sorted white-ball columns + special, unique `(Game, DrawDate)`.
- Committed real-history snapshots seed on first boot behind the import ledger;
  era-validated end to end.
- Endpoints: `next-draw`, `latest` (with `Pending`), `draws`, `check` (GET),
  `rule-eras`, `generate`, `healthz`; per-IP rate limiting; OpenAPI.
- Phases 2-4 as in D18; hosting as in D15; automation as in D16/D17 plus
  CodeQL, Dependabot, secret scanning, push protection from day zero.
