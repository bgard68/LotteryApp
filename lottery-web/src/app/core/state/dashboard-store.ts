import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Countdown, countdownTo } from '../domain/countdown';
import { GAMES, Game, GameMeta } from '../domain/game';
import { ApiUnreachableError, LatestDrawDto, LotteryApi, NextDrawDto } from '../ports/lottery-api';
import { CLOCK } from '../ports/clock';

export interface GameCardView {
  readonly meta: GameMeta;
  readonly loaded: boolean;
  readonly next: NextDrawDto | null;
  readonly latest: LatestDrawDto | null;
  readonly countdown: Countdown | null;
  readonly error: string | null;
}

/** Loads both games' dashboard data and ticks the countdowns off the CLOCK port. */
@Injectable({ providedIn: 'root' })
export class DashboardStore {
  private readonly api = inject(LotteryApi);
  private readonly clock = inject(CLOCK);

  private readonly now = signal(this.clock());
  private readonly next = signal<Partial<Record<Game, NextDrawDto>>>({});
  private readonly latest = signal<Partial<Record<Game, LatestDrawDto>>>({});
  private readonly errors = signal<Partial<Record<Game, string>>>({});
  private readonly loadedGames = signal<ReadonlySet<Game>>(new Set());

  readonly cards = computed<GameCardView[]>(() => {
    const now = this.now();
    return GAMES.map((meta) => {
      const next = this.next()[meta.game] ?? null;
      return {
        meta,
        loaded: this.loadedGames().has(meta.game),
        next,
        latest: this.latest()[meta.game] ?? null,
        countdown: next ? countdownTo(Date.parse(next.drawTimeUtc), now) : null,
        error: this.errors()[meta.game] ?? null,
      };
    });
  });

  constructor() {
    const timer = setInterval(() => this.now.set(this.clock()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    for (const { game } of GAMES) void this.load(game);
  }

  async load(game: Game): Promise<void> {
    try {
      const [next, latest] = await Promise.all([this.api.nextDraw(game), this.api.latest(game)]);
      this.next.update((m) => ({ ...m, [game]: next }));
      this.latest.update((m) => ({ ...m, [game]: latest }));
      this.errors.update((m) => ({ ...m, [game]: undefined }));

      // A countdown that hits zero means a drawing just happened - refetch
      // shortly after so the card flips to Pending/Published on its own.
      const msToDraw = Date.parse(next.drawTimeUtc) - this.clock();
      if (msToDraw > 0) setTimeout(() => void this.load(game), msToDraw + 30_000);
    } catch (e) {
      const message = e instanceof ApiUnreachableError
        ? "Can't reach the lottery API. If you're running locally, start the backend first."
        : 'Could not load drawing data.';
      this.errors.update((m) => ({ ...m, [game]: message }));
    } finally {
      this.loadedGames.update((s) => new Set(s).add(game));
    }
  }
}
