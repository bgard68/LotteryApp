import { Injectable, computed, inject, signal } from '@angular/core';
import { Game } from '../domain/game';
import { ApiUnreachableError, CheckResultDto, LotteryApi, RuleEraDto } from '../ports/lottery-api';

/** Ticket-checker state: current picks, era-driven validation, results. */
@Injectable({ providedIn: 'root' })
export class CheckerStore {
  private readonly api = inject(LotteryApi);

  readonly game = signal<Game>('powerball');
  readonly whites = signal<(number | null)[]>([null, null, null, null, null]);
  readonly special = signal<number | null>(null);
  readonly era = signal<RuleEraDto | null>(null);
  readonly result = signal<CheckResultDto | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  /** Client-side validation mirrors the server's (which stays authoritative). */
  readonly validationError = computed<string | null>(() => {
    const era = this.era();
    const whites = this.whites();
    const special = this.special();
    if (!era) return null;
    const filled = whites.filter((w): w is number => w != null);
    if (filled.length < 5 || special == null) return null; // incomplete, not invalid
    if (new Set(filled).size !== 5) return 'White balls must be distinct.';
    if (filled.some((w) => w < 1 || w > era.whiteBallMax))
      return `White balls must be between 1 and ${era.whiteBallMax}.`;
    if (special < 1 || special > era.specialBallMax)
      return `The special ball must be between 1 and ${era.specialBallMax}.`;
    return null;
  });

  readonly canCheck = computed(() =>
    !this.busy()
    && this.validationError() === null
    && this.whites().every((w) => w != null)
    && this.special() != null);

  constructor() {
    void this.loadEra();
  }

  async setGame(game: Game): Promise<void> {
    this.game.set(game);
    this.result.set(null);
    this.error.set(null);
    await this.loadEra();
  }

  setWhite(index: number, value: number | null): void {
    this.whites.update((w) => w.map((v, i) => (i === index ? value : v)));
    this.result.set(null);
  }

  setSpecial(value: number | null): void {
    this.special.set(value);
    this.result.set(null);
  }

  async generate(): Promise<void> {
    await this.run(async () => {
      const picks = await this.api.generate(this.game());
      this.whites.set(picks.whiteBalls);
      this.special.set(picks.special);
      this.result.set(null);
    });
  }

  async check(): Promise<void> {
    const whites = this.whites();
    const special = this.special();
    if (!this.canCheck() || special == null) return;
    await this.run(async () => {
      this.result.set(await this.api.check(this.game(), whites as number[], special));
    });
  }

  private async loadEra(): Promise<void> {
    try {
      const eras = await this.api.ruleEras(this.game());
      this.era.set(eras.find((e) => e.isCurrent) ?? null);
    } catch {
      this.era.set(null);
    }
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (e) {
      // Unreachable backend gets an actionable message - locally it means
      // the API process isn't running, not that anything is broken here.
      this.error.set(e instanceof ApiUnreachableError
        ? "Can't reach the lottery API. If you're running locally, start the backend first."
        : 'Something went wrong - try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
