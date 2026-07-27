import { Injectable, computed, inject, signal } from '@angular/core';
import { Game } from '../domain/game';
import { ApiUnreachableError, CheckResultDto, LotteryApi, RateLimitedError, RuleEraDto } from '../ports/lottery-api';

/** One editable ticket row; null slots are simply not filled in yet. */
export interface TicketDraft {
  whites: (number | null)[];
  special: number | null;
}

export const MIN_TICKETS = 1;
export const MAX_TICKETS = 10;

function emptyTicket(): TicketDraft {
  return { whites: [null, null, null, null, null], special: null };
}

function isCheckable(ticket: TicketDraft, era: RuleEraDto | null): boolean {
  if (ticket.special == null || ticket.whites.some((w) => w == null)) return false;
  const whites = ticket.whites as number[];
  if (new Set(whites).size !== 5) return false;
  if (!era) return true; // the server validates authoritatively anyway
  return whites.every((w) => w >= 1 && w <= era.whiteBallMax)
    && ticket.special >= 1 && ticket.special <= era.specialBallMax;
}

/** Ticket-checker state: 1-10 ticket drafts, era-driven validation, per-ticket results. */
@Injectable({ providedIn: 'root' })
export class CheckerStore {
  private readonly api = inject(LotteryApi);

  readonly game = signal<Game>('powerball');
  readonly tickets = signal<TicketDraft[]>([emptyTicket()]);
  readonly era = signal<RuleEraDto | null>(null);
  /** Which ticket to check and show: a zero-based index, or every ticket. */
  readonly selectedTicket = signal<number | 'all'>('all');
  /** Parallel to tickets; null until a check has run; unselected tickets stay null. */
  readonly results = signal<(CheckResultDto | null)[] | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  /** Matches shown per ticket; results stay fully loaded, this only slices the view. */
  readonly pageSize = signal<number | 'all'>(10);

  readonly count = computed(() => this.tickets().length);

  isSelected(index: number): boolean {
    const selected = this.selectedTicket();
    return selected === 'all' || selected === index;
  }

  /** Client-side validation mirrors the server's (which stays authoritative). */
  readonly validationError = computed<string | null>(() => {
    const era = this.era();
    if (!era) return null;
    const selected = this.selectedTicket();

    for (const [index, ticket] of this.tickets().entries()) {
      if (selected !== 'all' && selected !== index) continue;
      const filled = ticket.whites.filter((w): w is number => w != null);
      if (filled.length < 5 || ticket.special == null) continue; // incomplete, not invalid
      const label = this.tickets().length > 1 ? `Ticket ${index + 1}: ` : '';
      if (new Set(filled).size !== 5) return `${label}white balls must be distinct.`;
      if (filled.some((w) => w < 1 || w > era.whiteBallMax))
        return `${label}white balls must be between 1 and ${era.whiteBallMax}.`;
      if (ticket.special < 1 || ticket.special > era.specialBallMax)
        return `${label}the special ball must be between 1 and ${era.specialBallMax}.`;
    }
    return null;
  });

  readonly allComplete = computed(() => this.tickets()
    .filter((_, i) => this.isSelected(i))
    .every((t) => t.special != null && t.whites.every((w) => w != null)));

  readonly canCheck = computed(() =>
    !this.busy() && this.allComplete() && this.validationError() === null);

  constructor() {
    void this.loadEra();
  }

  async setGame(game: Game): Promise<void> {
    this.game.set(game);
    this.results.set(null);
    this.error.set(null);
    await this.loadEra();
  }

  setCount(raw: number): void {
    const count = Math.min(MAX_TICKETS, Math.max(MIN_TICKETS, Math.floor(raw) || MIN_TICKETS));
    this.tickets.update((tickets) => {
      const next = tickets.slice(0, count);
      while (next.length < count) next.push(emptyTicket());
      return next;
    });
    // A selection pointing past the new count would silently check nothing.
    const selected = this.selectedTicket();
    if (selected !== 'all' && selected >= count) this.selectedTicket.set('all');
    this.results.set(null);
  }

  // Selection is a VIEW filter: every complete ticket is checked regardless
  // (so big wins can surface on unselected tickets), so switching the
  // selection never discards results.
  setSelectedTicket(value: number | 'all'): void {
    this.selectedTicket.set(value);
  }

  setWhite(ticket: number, index: number, value: number | null): void {
    this.tickets.update((tickets) => tickets.map((t, ti) =>
      ti === ticket ? { ...t, whites: t.whites.map((v, i) => (i === index ? value : v)) } : t));
    this.results.set(null);
  }

  setPageSize(value: number | 'all'): void {
    this.pageSize.set(value);
  }

  setSpecial(ticket: number, value: number | null): void {
    this.tickets.update((tickets) => tickets.map((t, ti) =>
      ti === ticket ? { ...t, special: value } : t));
    this.results.set(null);
  }

  async generate(): Promise<void> {
    await this.run(async () => {
      const picks = await this.api.generate(this.game(), this.count());
      this.tickets.set(picks.tickets.map((t) => ({ whites: [...t.whiteBalls], special: t.special })));
      this.results.set(null);
    });
  }

  async check(): Promise<void> {
    if (!this.canCheck()) return;
    const game = this.game();
    const era = this.era();
    const tickets = this.tickets();
    await this.run(async () => {
      // Every complete, valid ticket is checked - not just the selected one -
      // so the big-wins panel always covers the whole set. Incomplete or
      // invalid unselected tickets are simply skipped.
      const results = await Promise.all(tickets.map((t) =>
        isCheckable(t, era)
          ? this.api.check(game, t.whites as number[], t.special as number)
          : Promise.resolve<CheckResultDto | null>(null)));
      this.results.set(results);
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
      // Classified transport errors get actionable messages; everything else
      // stays generic.
      this.error.set(
        e instanceof RateLimitedError
          ? 'Checking a little too fast - wait a few seconds and try again.'
          : e instanceof ApiUnreachableError
            ? "Can't reach the lottery API. If you're running locally, start the backend first."
            : 'Something went wrong - try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
