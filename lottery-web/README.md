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
â”œâ”€â”€ core/
â”‚   â”œâ”€â”€ domain/     # pure TS, framework-free: game metadata, countdown math,
â”‚   â”‚               #   jackpot formatting (all unit-tested without Angular)
â”‚   â”œâ”€â”€ ports/      # abstractions the UI depends on (DIP):
â”‚   â”‚               #   LotteryApi (abstract class) + CLOCK (injectable "now")
â”‚   â”œâ”€â”€ data/       # HttpLotteryApi - the HTTP adapter, bound in app.config.ts
â”‚   â”œâ”€â”€ api/        # schema.d.ts - types GENERATED from the backend's OpenAPI
â”‚   â””â”€â”€ state/      # signal stores: DashboardStore (cards + ticking countdown),
â”‚                   #   CheckerStore (picks, era validation, results)
â”œâ”€â”€ ui/             # dumb presentational components - no store/HTTP access:
â”‚                   #   game-card, number-balls; ticket-checker is the feature edge
â””â”€â”€ app.ts          # the only smart shell; app.config.ts = composition root
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

**Both processes must run.** The frontend has no data of its own - every card
and button goes through the dev proxy to the API on :5090.

## Troubleshooting

### "Can't reach the lottery API. If you're running locally, start the backend first."

Exactly what it says: the Angular dev server is up but the .NET API is not
(or is on a different port than `proxy.conf.json` targets). Start the API
(terminal 1 above) and retry - no restart of the frontend needed.

History of this message: it used to be a generic "Something went wrong - try
again", which hid the real cause when clicking **Generate picks** with the
backend down. Root cause found while debugging: when the proxied backend is
unreachable, Vite's dev proxy responds **500 with an empty body** (not the
502/504 a classic gateway returns), so naive status checks miss it. The fix:
the HTTP adapter classifies unreachable-backend responses (status 0, 502,
503, 504, or 500-with-empty-body - a real API 500 always carries a problem
body) into a typed `ApiUnreachableError`, and the stores map that to the
actionable message. Spec-covered in `checker-store.spec.ts`.

### Dashboard cards show the same message

Same cause, same fix - the cards load `/next-draw` and `/latest` through the
same adapter.

## Tests

```bash
npx ng test --watch=false --browsers=ChromeHeadless
```

19 specs: pure domain (countdown split/format/clamp, jackpot formatting incl.
null-hides), CheckerStore against an in-memory `FakeLotteryApi` (era load,
out-of-era/duplicate rejection, incomplete-ticket quiescence, check/generate
through the port, unreachable-vs-generic error messaging), and an App shell
render smoke test. No HTTP mocking anywhere - the port abstraction makes
fakes trivial.

## Generated API types

`core/api/schema.d.ts` is generated from the running backend:

```bash
# with the API running on :5090
npx openapi-typescript http://localhost:5090/openapi/v1.json -o src/app/core/api/schema.d.ts
```

Phase 4 adds the CI drift check (regenerate + fail on diff).
