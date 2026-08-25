import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { FakeLotteryApi } from './core/state/fake-lottery-api';
import { FakeViewport } from './core/state/fake-viewport';
import { LotteryApi } from './core/ports/lottery-api';
import { Viewport } from './core/ports/viewport';
import { CheckerStore } from './core/state/checker-store';

/** Builds the shell at a pinned viewport - never at whatever size Karma opened. */
async function renderApp(isMobile: boolean) {
  const viewport = new FakeViewport(isMobile);
  await TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideZonelessChangeDetection(),
      { provide: LotteryApi, useValue: new FakeLotteryApi() },
      { provide: Viewport, useValue: viewport },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(App);
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, viewport, el: fixture.nativeElement as HTMLElement };
}

function tab(el: HTMLElement, label: string): HTMLButtonElement {
  return Array.from(el.querySelectorAll('.tabbar button'))
    .find((b) => b.textContent?.includes(label)) as HTMLButtonElement;
}

describe('App', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('desktop', () => {
    it('renders the dashboard shell with both game cards and the footer disclaimer', async () => {
      const { el } = await renderApp(false);

      expect(el.querySelector('h1')?.textContent).toContain('Lucky numbers');
      expect(el.querySelectorAll('app-game-card').length).toBe(2);
      expect(el.querySelector('app-ticket-checker')).toBeTruthy();
      expect(el.querySelector('footer')?.textContent).toContain('not affiliated');
    });

    it('shows everything on one page with no tab bar', async () => {
      const { el } = await renderApp(false);

      expect(el.querySelector('.tabbar')).toBeNull();
      expect(el.querySelectorAll('app-game-card').length).toBe(2);
      expect(el.querySelector('app-ticket-checker')).toBeTruthy();
    });
  });

  describe('phone', () => {
    it('starts on the games tab with the checker hidden', async () => {
      const { el } = await renderApp(true);

      expect(el.querySelector('.tabbar')).toBeTruthy();
      expect(el.querySelectorAll('app-game-card').length).toBe(2);
      expect(el.querySelector('app-ticket-checker')).toBeNull();
    });

    it('swaps the cards for the checker when the Tickets tab is picked', async () => {
      const { fixture, el } = await renderApp(true);

      const tickets = tab(el, 'Tickets');
      tickets.click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelectorAll('app-game-card').length).toBe(0);
      expect(el.querySelector('app-ticket-checker')).toBeTruthy();
      expect(tickets.getAttribute('aria-current')).toBe('page');
    });

    it('jumps to the wins tab once results arrive', async () => {
      const { fixture, el } = await renderApp(true);
      const store = TestBed.inject(CheckerStore);

      store.tickets.set([{ whites: [1, 2, 3, 4, 5], special: 6, selected: true }]);
      await store.check();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(tab(el, 'Wins').getAttribute('aria-current')).toBe('page');
      expect(el.querySelector('app-ticket-checker')).toBeTruthy();
    });

    it('returns to the single-page layout when the viewport widens again', async () => {
      const { fixture, el, viewport } = await renderApp(true);

      tab(el, 'Tickets').click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.querySelectorAll('app-game-card').length).toBe(0);

      viewport.set(false);
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelector('.tabbar')).toBeNull();
      expect(el.querySelectorAll('app-game-card').length).toBe(2);
      expect(el.querySelector('app-ticket-checker')).toBeTruthy();
    });
  });
});
