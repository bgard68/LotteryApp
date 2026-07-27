import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { formatCountdown } from '../../core/domain/countdown';
import { formatJackpot } from '../../core/domain/money';
import { GameCardView } from '../../core/state/dashboard-store';
import { NumberBalls } from '../number-balls/number-balls';

/** Dumb card for one game: jackpot, countdown, last drawing. No store access. */
@Component({
  selector: 'app-game-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NumberBalls],
  templateUrl: './game-card.html',
  styleUrl: './game-card.scss',
})
export class GameCard {
  readonly view = input.required<GameCardView>();

  protected readonly jackpot = computed(() => formatJackpot(this.view().next?.estimatedJackpot));
  protected readonly cashValue = computed(() => formatJackpot(this.view().next?.cashValue));
  protected readonly lastJackpot = computed(() => formatJackpot(this.view().latest?.jackpotAmount));
  protected readonly countdownText = computed(() => {
    const c = this.view().countdown;
    return c ? formatCountdown(c) : null;
  });
}
