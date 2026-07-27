import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CheckerStore } from '../../core/state/checker-store';
import { GAMES } from '../../core/domain/game';
import { formatJackpot } from '../../core/domain/money';

/**
 * Ticket checker panel. This component talks to CheckerStore directly (it is
 * the feature's smart edge); the inputs/results markup stays presentational.
 */
@Component({
  selector: 'app-ticket-checker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  templateUrl: './ticket-checker.html',
  styleUrl: './ticket-checker.scss',
})
export class TicketChecker {
  protected readonly store = inject(CheckerStore);
  protected readonly games = GAMES;
  protected readonly formatJackpot = formatJackpot;

  protected onNumber(index: number | 'special', raw: string): void {
    const value = raw === '' ? null : Number(raw);
    const parsed = value != null && Number.isInteger(value) ? value : null;
    if (index === 'special') this.store.setSpecial(parsed);
    else this.store.setWhite(index, parsed);
  }
}
