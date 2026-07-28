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
│   ├── ports/      # abstractions the UI depends on (DIP): LotteryApi
│   │               #   (abstract class), CLOCK (injectable "now"),
│   │               #   Viewport (injectable "is this a phone")
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
- **Jackpot amounts degrade to hidden**: any missing jackpot value (see
  [data sources](../docs/DATA-SOURCES.md)) simply omits the amount - the card
  never breaks.

## Checker behaviour

- **1-10 tickets** (count selector, clamped); Generate fills every row with
  era-valid picks from the API; all rows stay hand-editable.
- **Checkboxes are a view filter**: with 2+ tickets each row gets a checkbox
  (all checked by default) controlling which tickets' detailed match lists
  display. "Check history" still checks **every complete ticket** behind the
  scenes, so the big-wins panel always covers the whole set - including
  unchecked tickets - and toggling a checkbox never discards results.
- **Big wins** = 3 or more matching *white* balls plus the special ball (the
  $100 tier and up). Qualifying wins surface in a highlighted panel with the
  drawing's numbers and payout, and the winning ticket's row is highlighted.
- **History rows show the drawing's numbers** (not the ticket repeated), with
  the balls that appear on your ticket flashing; a "Show 10/25/50/100/All
  matches per ticket" selector slices the fully-loaded results client-side.

## Responsive layout ([D22](../docs/REQUIREMENTS-AND-DECISIONS.md))

**At 641px and up nothing changed** - the same 880px single-scroll page, the
same element sizes. Below 640px the shell becomes a three-tab app:

| Tab | Shows |
|---|---|
| Games | Both game cards, one per row |
| Check | Generate controls and the ticket rows |
| Wins | The big-wins panel and per-ticket match history |

- **`Viewport` is a port**, not a `matchMedia` call inside a component: an
  injectable exposing an `isMobile` signal, with `BrowserViewport` as the
  media-query adapter and `FakeViewport` for specs. Same DIP as `CLOCK` -
  tests pin a layout instead of resizing a real browser.
- **Checking hands off to the Wins tab** and badges the big-win count -
  otherwise the answer renders on a screen the user is not looking at. The
  threshold moved into `CheckerStore.bigWins`, so the panel and the badge
  cannot disagree.
- **`TicketChecker` takes a `section` input** (`all` | `entry` | `results`)
  defaulting to `all`, so the desktop path renders exactly as before while the
  phone layout splits one component across two tabs.
- **Entry sizing is the real constraint**: six fixed-width number inputs
  overflow a 320px screen, so on mobile they flex to share the row and the
  balls shrink from 2.2rem to 1.95rem. Input font-size stays at 1rem - any
  smaller and iOS Safari zooms the page on focus.
- **Match rows stack** (date, then balls, then payout) instead of competing
  for one line.
- The tab bar is `position: fixed` with `env(safe-area-inset-bottom)` padding
  and 44px touch targets; `main` reserves matching bottom padding so the
  footer is never trapped underneath it.

## API origin

Requests go to `{API_BASE_URL}/api/*`. The value is resolved **at runtime**
from `config.json` before bootstrap (`core/ports/api-base-url.ts`), defaulting
to an empty string - meaning same origin, which is correct locally where the
dev proxy handles `/api`. In Azure the deploy workflow writes that file from
the `API_BASE_URL` repository variable, because the free Static Web Apps SKU
has no linked-backend proxy and the browser must call the App Service origin
directly. One build artefact therefore deploys to any environment; see
[docs/AZURE-DEPLOYMENT.md](../docs/AZURE-DEPLOYMENT.md).

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

30 specs: pure domain (countdown split/format/clamp, jackpot formatting incl.
null-hides), CheckerStore against an in-memory `FakeLotteryApi` (era load,
count clamping, per-ticket validation naming the offending ticket,
checkbox-selection semantics - all-complete-tickets checked, view-filter
toggling keeps results, no-selection disables checking - page-size behaviour,
and rate-limited/unreachable/generic error messaging), and App shell specs
covering **both layouts** against a `FakeViewport`: desktop renders everything
with no tab bar; phone starts on Games, swaps to the checker on Check, jumps
to Wins when results arrive, and returns to the single page when the viewport
widens. No HTTP mocking anywhere - the port abstraction makes fakes trivial.

## Workflows on this branch

`main` and `frontend` are never merged, so this branch carries only the
workflows that can fire here: `ci.yml`, `ci-frontend.yml` (specs, build,
OpenAPI drift check), `codeql.yml` (JavaScript/TypeScript - the C# analysis
lives on `main`), `gitleaks.yml`, and `deploy-web.yml`.

**Deployment is automatic**: pushing changes under `lottery-web/**` builds,
tests, writes `config.json` from the `API_BASE_URL` variable, and deploys to
Azure Static Web Apps - independently of the API, which deploys from `main`
([D15](../docs/REQUIREMENTS-AND-DECISIONS.md)).

## Generated API types

`core/api/schema.d.ts` is generated from the running backend:

```bash
# with the API running on :5090
npx openapi-typescript http://localhost:5090/openapi/v1.json -o src/app/core/api/schema.d.ts
```

Phase 4 adds the CI drift check (regenerate + fail on diff).
