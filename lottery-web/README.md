# lottery-web (the Angular frontend)

Angular 20 dashboard: game cards with jackpots and live countdowns, last
winning numbers, and the ticket checker. It lives alongside the API in this
repository; the two deploy independently, decided by path filters rather than
by living on separate branches.

Back to the [repository README](../README.md).

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

The full picture - what resolves before bootstrap, one interaction traced from
click to API, why the single RxJS call lives where it does, and how signals
drive rendering without a zone - is in **[ARCHITECTURE.md](ARCHITECTURE.md)**.

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
- **A colour legend sits above the results**, not below them: the balls carry
  meaning in colour alone, so the key that decodes them has to be on screen
  before the first row that uses it. The swatches reuse the ball styling and
  the same 1s pulse, and the special-ball label follows the game ("the
  Powerball" / "the Mega Ball").

## Responsive layout ([D23](../docs/REQUIREMENTS-AND-DECISIONS.md))

**At 641px and up nothing changed** - the same 880px single-scroll page, the
same element sizes. Below 640px the shell becomes a three-tab app:

| Tab | Shows |
|---|---|
| Games | Both game cards, one per row |
| Tickets | Generate controls and the ticket rows |
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

## Security headers

`public/staticwebapp.config.json` sets a Content Security Policy, `X-Frame-Options`,
`X-Content-Type-Options`, `Referrer-Policy` and a `Permissions-Policy` on every
response. Before it existed the site had **no CSP and no anti-framing** - Static
Web Apps supplies HSTS and nosniff by default, and nothing else.

Two things the policy has to get right, both of which would break the app rather
than fail visibly:

- **`connect-src` must name the API origin.** The free SWA SKU has no linked
  backend, so the browser calls App Service directly - a policy without it blocks
  every request the app makes.
- **`style-src` needs `'unsafe-inline'`.** Angular injects component styles as
  inline `<style>` elements; without it the page renders unstyled.
- **`inlineCritical` is off.** Angular's critical-CSS optimization lazy-loads
  the full stylesheet via an inline `onload` handler - which `script-src 'self'`
  blocks, leaving the stylesheet stuck at `media="print"`. At a 767-byte
  stylesheet the optimization buys nothing; `check:swa` fails the build if an
  inline handler ever reappears in the shipped HTML.

`npm run check:swa` asserts the headers survived, and CI runs it against the
**built output** rather than the source. That is the failure this guards: the
file only reaches Azure if the asset pipeline copies it, and a site deployed
without it works perfectly - nothing else would notice.

## Link previews

`index.html` carries Open Graph and Twitter card tags, and `public/social-card.jpg`
(1200x627, the 1.91:1 ratio unfurlers crop to) ships with the build. Without them a
shared URL renders as a bare title and no image on LinkedIn, Slack and iMessage.

Two details that are easy to get wrong:

- **`og:url` and `og:image` must be absolute.** Every crawler ignores relative
  paths, so a `/social-card.jpg` that looks fine in the browser silently yields
  no preview image.
- **The image is served from this origin**, not from the repository or a CDN, so
  it deploys and versions with the app rather than drifting independently.

Both values hard-code the Static Web Apps hostname. A custom domain means editing
them - there is no runtime substitution, because crawlers read the served HTML and
never execute the app.

## API origin

Requests go to `{API_BASE_URL}/api/*`. The value is resolved **at runtime**
from `config.json` before bootstrap (`core/ports/api-base-url.ts`), defaulting
to an empty string - meaning same origin, which is correct locally where the
dev proxy handles `/api`. In Azure the deploy workflow writes that file from
the `API_BASE_URL` repository variable, because the free Static Web Apps SKU
has no linked-backend proxy and the browser must call the App Service origin
directly. One build artefact therefore deploys to any environment; see
[docs/AZURE-DEPLOYMENT.md](../docs/AZURE-DEPLOYMENT.md).

## URL construction

The HTTP adapter encodes **by construction**: path segments through
`encodeURIComponent`, query values through `HttpParams`. Nothing is pasted into
a template string.

The reason is narrow, because the obvious reading is that the types already
prevent this. They do not: the game reaches the store via
`store.setGame($any($event.target).value)`, and `$any` is a deliberate hole in
type checking. At runtime the `<select>` options are the only thing constraining
it. A path segment is where that matters - unencoded, a crafted value changes
*which* endpoint is called rather than what is asked of it.

The exposure is a user's own browser and the server 404s unknown games, so this
is hygiene rather than a live vulnerability. Fixed by construction anyway,
because "remember to encode" is not a control. `http-lottery-api.spec.ts` pins
it with a game value that tries to traverse out of its segment.

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

53 specs: pure domain (countdown split/format/clamp, jackpot formatting incl.
null-hides), CheckerStore against an in-memory `FakeLotteryApi` (era load,
count clamping, per-ticket validation naming the offending ticket,
checkbox-selection semantics - all-complete-tickets checked, view-filter
toggling keeps results, no-selection disables checking - page-size behaviour,
and rate-limited/unreachable/generic error messaging, plus the **big-win
threshold** at its boundaries - 3 whites plus the special qualifies, 2 plus
the special does not, 5 whites without the special does not, unchecked
tickets still count, and an edit clears the list), and App shell specs
covering **both layouts** against a `FakeViewport`: desktop renders everything
with no tab bar; phone starts on Games, swaps to the checker on Check, jumps
to Wins when results arrive, and returns to the single page when the viewport
widens. No HTTP mocking anywhere - the port abstraction makes fakes trivial.

## Workflows

`ci-frontend.yml` runs the specs, the production build and the OpenAPI drift
check. `codeql.yml` analyzes JavaScript/TypeScript alongside C# in one matrix.
`gitleaks.yml` and `dependency-review.yml` cover both halves.

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
