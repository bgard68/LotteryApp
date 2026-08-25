import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BrowserViewport, MOBILE_MAX_WIDTH, Viewport } from './viewport';

/**
 * The one place stubbing matchMedia is right: this adapter exists to speak it,
 * so the query it asks for and the listener it keeps ARE the behaviour under
 * test. Every other spec pins the layout through FakeViewport instead.
 */
describe('BrowserViewport', () => {
  let queries: string[];
  let fireChange: (matches: boolean) => void;

  /** Stands in for the real MediaQueryList so the spec drives the breakpoint. */
  function stubMatchMedia(matches: boolean): void {
    queries = [];
    fireChange = () => fail('nothing subscribed to the media query');
    spyOn(window, 'matchMedia').and.callFake((query: string) => {
      queries.push(query);
      return {
        matches,
        addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => {
          fireChange = (next) => listener({ matches: next } as MediaQueryListEvent);
        },
      } as unknown as MediaQueryList;
    });
  }

  afterEach(() => TestBed.resetTestingModule());

  it('asks for the phone-width breakpoint, not a hand-typed pixel count', () => {
    stubMatchMedia(false);

    new BrowserViewport();

    expect(queries).toEqual([`(max-width: ${MOBILE_MAX_WIDTH}px)`]);
  });

  it('starts mobile when the app opens on a phone-sized screen', () => {
    stubMatchMedia(true);

    expect(new BrowserViewport().isMobile()).toBeTrue();
  });

  it('starts desktop when it does not', () => {
    stubMatchMedia(false);

    expect(new BrowserViewport().isMobile()).toBeFalse();
  });

  it('follows the query afterwards, so rotating a phone re-lays out', () => {
    stubMatchMedia(false);
    const viewport = new BrowserViewport();

    fireChange(true);
    expect(viewport.isMobile()).toBeTrue();

    fireChange(false);
    expect(viewport.isMobile()).toBeFalse();
  });

  // The guard is what keeps this class constructible off a real browser tab -
  // a plain unit test or a future prerender step.
  it('stays constructible, and desktop, where matchMedia does not exist', () => {
    const host = window as { matchMedia?: typeof window.matchMedia };
    const real = window.matchMedia;
    host.matchMedia = undefined;

    try {
      expect(new BrowserViewport().isMobile()).toBeFalse();
    } finally {
      host.matchMedia = real;
    }
  });

  it('is what the Viewport port hands out when nothing overrides it', () => {
    stubMatchMedia(false);
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });

    expect(TestBed.inject(Viewport)).toBeInstanceOf(BrowserViewport);
  });
});
