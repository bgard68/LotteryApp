import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DashboardStore } from './core/state/dashboard-store';
import { GameCard } from './ui/game-card/game-card';
import { TicketChecker } from './ui/ticket-checker/ticket-checker';

/** The only smart shell: wires the dashboard store to the presentational UI. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GameCard, TicketChecker],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly dashboard = inject(DashboardStore);
}
