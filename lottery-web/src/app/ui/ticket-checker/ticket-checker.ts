import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CheckerStore, MAX_TICKETS, MIN_TICKETS, TicketDraft } from '../../core/state/checker-store';
import { GAMES } from '../../core/domain/game';
import { formatJackpot } from '../../core/domain/money';
import { TicketMatchDto } from '../../core/ports/lottery-api';
import { NumberBalls } from '../number-balls/number-balls';

/**
 * Ticket checker panel. This component talks to CheckerStore directly (it is
 * the feature's smart edge); the inputs/results markup stays presentational.
 */
@Component({
  selector: 'app-ticket-checker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NumberBalls],
  templateUrl: './ticket-checker.html',
  styleUrl: './ticket-checker.scss',
})
export class TicketChecker {
  protected readonly store = inject(CheckerStore);
  protected readonly games = GAMES;
  protected readonly formatJackpot = formatJackpot;
  protected readonly minTickets = MIN_TICKETS;
  protected readonly maxTickets = MAX_TICKETS;

  protected onNumber(ticket: number, index: number | 'special', raw: string): void {
    const value = raw === '' ? null : Number(raw);
    const parsed = value != null && Number.isInteger(value) ? value : null;
    if (index === 'special') this.store.setSpecial(ticket, parsed);
    else this.store.setWhite(ticket, index, parsed);
  }

  protected onCount(raw: string): void {
    this.store.setCount(Number(raw));
  }

  protected accent(): string {
    return this.store.game() === 'powerball' ? 'pb' : 'mm';
  }

  /** The ticket's white values that appear in the drawing - these flash. */
  protected matchedWhites(ticket: TicketDraft, match: TicketMatchDto): number[] {
    return ticket.whites.filter((w): w is number => w != null && match.drawnWhiteBalls.includes(w));
  }

  protected winningMatches(ticketIndex: number): TicketMatchDto[] {
    return this.store.results()?.[ticketIndex]?.matches ?? [];
  }

  /** Slice per the "show per ticket" selector; results stay fully loaded. */
  protected visibleMatches(ticketIndex: number): TicketMatchDto[] {
    const matches = this.winningMatches(ticketIndex);
    const size = this.store.pageSize();
    return size === 'all' ? matches : matches.slice(0, size);
  }

  protected onPageSize(raw: string): void {
    this.store.setPageSize(raw === 'all' ? 'all' : Number(raw));
  }
}
