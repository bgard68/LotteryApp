import { Injectable, Signal, signal } from '@angular/core';

/** Phone-width breakpoint. Below this the shell switches to the tabbed layout. */
export const MOBILE_MAX_WIDTH = 640;

/**
 * Is the app rendering on a phone-sized screen? A port rather than a raw
 * `matchMedia` call in the shell, for the same reason CLOCK and LotteryApi are
 * ports: specs pin the layout instead of resizing a real browser window (DIP).
 */
@Injectable({ providedIn: 'root', useFactory: () => new BrowserViewport() })
export abstract class Viewport {
  abstract readonly isMobile: Signal<boolean>;
}

/** The real adapter: a media-query listener, so rotating a phone re-lays out. */
export class BrowserViewport implements Viewport {
  private readonly state = signal(false);
  readonly isMobile = this.state.asReadonly();

  constructor() {
    // Guarded rather than assumed: keeps the class constructible in any
    // non-browser context (a plain unit test, a future prerender step).
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return;

    const query = window.matchMedia(`(max-width: ${MOBILE_MAX_WIDTH}px)`);
    this.state.set(query.matches);
    query.addEventListener('change', (event) => this.state.set(event.matches));
  }
}
