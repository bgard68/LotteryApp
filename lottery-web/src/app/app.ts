import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { CheckerStore } from './core/state/checker-store';
import { DashboardStore } from './core/state/dashboard-store';
import { Viewport } from './core/ports/viewport';
import { GameCard } from './ui/game-card/game-card';
import { CheckerSection, TicketChecker } from './ui/ticket-checker/ticket-checker';

/** Sections of the phone layout. Desktop shows all of them on one page. */
export type MobileTab = 'games' | 'tickets' | 'wins';

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
  protected readonly checker = inject(CheckerStore);
  protected readonly viewport = inject(Viewport);

  protected readonly tab = signal<MobileTab>('games');

  // On desktop every section renders, exactly as before - the tab only
  // narrows what is visible once the viewport is phone-sized.
  protected readonly showGames = computed(() => !this.viewport.isMobile() || this.tab() === 'games');
  protected readonly showChecker = computed(() => !this.viewport.isMobile() || this.tab() !== 'games');
  protected readonly checkerSection = computed<CheckerSection>(() =>
    !this.viewport.isMobile() ? 'all' : this.tab() === 'tickets' ? 'entry' : 'results');

  protected readonly winCount = computed(() => this.checker.bigWins().length);

  constructor() {
    effect(() => {
      // Results arriving must bring the user with them: on a phone the answer
      // renders on a tab they are not looking at otherwise.
      if (this.viewport.isMobile() && this.checker.results() !== null) this.tab.set('wins');
    });

    effect(() => {
      // Widening back to desktop shows everything anyway; reset so a later
      // narrowing starts on the dashboard rather than a stale tab.
      if (!this.viewport.isMobile()) this.tab.set('games');
    });
  }

  protected selectTab(tab: MobileTab): void {
    this.tab.set(tab);
    // Each tab is its own screen - landing mid-scroll reads as a broken page.
    if (typeof window !== 'undefined') window.scrollTo({ top: 0 });
  }
}
