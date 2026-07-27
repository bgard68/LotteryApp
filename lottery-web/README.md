# lottery-web (frontend - lives on the `frontend` branch only)

Angular 20 dashboard: game cards with jackpots and live countdowns, last
winning numbers, and the ticket checker. **This app is developed on the
`frontend` branch and is never merged into `main`** - `main` is the
backend/API branch.

Back to the [main README](../README.md).

## Architecture - the onion, in Angular idiom

Same dependency rule as the backend, enforced by folder discipline:

```
src/app/
├── core/
│   ├── domain/     # pure TS, framework-free: game metadata, countdown math,
│   │               #   jackpot formatting (all unit-tested without Angular)
│   ├── ports/      # abstractions the UI depends on (DIP):
│   │               #   LotteryApi (abstract class) + CLOCK (injectable "now")
│   ├── data/       # HttpLotteryApi - the HTTP adapter, bound in app.config.ts
│   ├── api/        # schema.d.ts - types GENERATED from the backend's OpenAPI
│   └── state/      # signal stores: DashboardStore (cards + ticking countdown),
│                   #   CheckerStore (picks, era validation, results)
├── ui/             # dumb presentational components - no store/HTTP access:
│                   #   game-card, number-balls; ticket-checker is the feature edge
└── app.ts          # the only smart shell; app.config.ts = composition root
```

- **Zoneless + signals + OnPush** throughout; no RxJS state (the only
  Observable is HttpClient's, unwrapped at the adapter).
- **`CLOCK` is the frontend's TimeProvider**: stores read "now" through an
  injected function, so countdown logic tests with frozen time.
- **Era-aware validation**: the checker fetches `/rule-eras` and validates
  picks client-side for instant feedback; the server remains authoritative
  (trust boundary - deliberate duplication, values still live in one place).
- **Countdown self-heals**: when a countdown hits zero the store refetches, so
  the card flips to "Results pending" and later to the new numbers on its own.
- **Jackpot nulls hide**: Powerball has no jackpot source (see
  [data sources](../docs/DATA-SOURCES.md)) - the card simply omits the amount.

## Dev

```bash
# terminal 1 - API
dotnet run --project ../src/Lottery.Api --urls http://localhost:5090
# terminal 2 - frontend (proxies /api to :5090 via proxy.conf.json)
npm start
```

## Tests

```bash
npx ng test --watch=false --browsers=ChromeHeadless
```

15 specs: pure domain (countdown split/format/clamp, jackpot formatting incl.
null-hides), CheckerStore against an in-memory `FakeLotteryApi` (era load,
out-of-era/duplicate rejection, incomplete-ticket quiescence, check/generate
through the port), and an App shell render smoke test. No HTTP mocking
anywhere - the port abstraction makes fakes trivial.

## Generated API types

`core/api/schema.d.ts` is generated from the running backend:

```bash
# with the API running on :5090
npx openapi-typescript http://localhost:5090/openapi/v1.json -o src/app/core/api/schema.d.ts
```

Phase 4 adds the CI drift check (regenerate + fail on diff).
