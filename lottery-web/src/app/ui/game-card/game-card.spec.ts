import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { GameCard } from './game-card';
import { GAMES } from '../../core/domain/game';
import { GameCardView } from '../../core/state/dashboard-store';

/**
 * The dashboard's headline. It is deliberately dumb - one input decides
 * everything - so the spec walks the states that input can be in: still
 * loading, failed, published, pending, and amounts the feed left out.
 */
describe('GameCard', () => {
  /** A fully loaded Powerball card; each test bends the one field it is about. */
  function loaded(overrides: Partial<GameCardView> = {}): GameCardView {
    return {
      meta: GAMES[0],
      loaded: true,
      next: {
        game: 'powerball',
        drawDate: '2026-07-27',
        drawTimeUtc: '2026-07-28T02:59:00+00:00',
        estimatedJackpot: 344_200_000,
        cashValue: 160_000_000,
      },
      latest: {
        game: 'powerball',
        status: 'Published',
        drawDate: '2026-07-25',
        whiteBalls: [3, 4, 24, 36, 47],
        special: 17,
        specialName: 'Powerball',
        jackpotAmount: null,
        jackpotWon: null,
      },
      countdown: { days: 1, hours: 4, minutes: 38, seconds: 12 },
      error: null,
      ...overrides,
    };
  }

  async function render(view: GameCardView): Promise<HTMLElement> {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });

    const fixture = TestBed.createComponent(GameCard);
    fixture.componentRef.setInput('view', view);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('leads with the jackpot, its cash value and the countdown', async () => {
    const el = await render(loaded());

    expect(el.querySelector('h2')?.textContent).toContain('Powerball');
    expect(el.querySelector('.amount')?.textContent).toContain('$344.2 million');
    expect(el.querySelector('.cash')?.textContent).toContain('$160 million');
    expect(el.querySelector('.countdown')?.textContent).toContain('1d 04:38:12');
    expect(el.querySelector('.when')?.textContent).toContain('Jul 27');
  });

  it('says Loading until the first answer arrives', async () => {
    const el = await render(loaded({ loaded: false, next: null, latest: null, countdown: null }));

    expect(el.querySelector('.muted')?.textContent).toContain('Loading');
    expect(el.querySelector('.amount')).toBeNull();
    expect(el.querySelector('.latest')).toBeNull();
  });

  it('shows the failure instead of a half-empty card', async () => {
    const el = await render(loaded({ error: 'Could not load drawing data.', next: null, latest: null, countdown: null }));

    expect(el.querySelector('.error')?.textContent).toContain('Could not load drawing data.');
    expect(el.querySelector('.next')).toBeNull();
    expect(el.querySelector('.latest')).toBeNull();
  });

  // `countdown` is typed independently of `next`, so the card must render the
  // drawing date on its own rather than assuming a timer is always available.
  it('still names the next drawing when there is no countdown to show', async () => {
    const el = await render(loaded({ countdown: null }));

    expect(el.querySelector('.when')?.textContent).toContain('Jul 27');
    expect(el.querySelector('.countdown')).toBeNull();
  });

  it('hides the jackpot lines when the feed carries no amount', async () => {
    const view = loaded();
    const el = await render({ ...view, next: { ...view.next!, estimatedJackpot: null, cashValue: null } });

    expect(el.querySelector('.amount')).toBeNull();
    expect(el.querySelector('.cash')).toBeNull();
    expect(el.querySelector('.when')?.textContent).toContain('Jul 27');
  });

  it('hides the cash value alone when only that is missing', async () => {
    const view = loaded();
    const el = await render({ ...view, next: { ...view.next!, cashValue: null } });

    expect(el.querySelector('.amount')?.textContent).toContain('$344.2 million');
    expect(el.querySelector('.cash')).toBeNull();
  });

  it('shows the drawn numbers once the drawing is published', async () => {
    const el = await render(loaded());

    expect(el.querySelectorAll('app-number-balls .ball').length).toBe(6);
    expect(el.querySelector('.latest')?.textContent).not.toContain('pending');
  });

  it('says results are pending while the drawing is still being reported', async () => {
    const view = loaded();
    const el = await render({
      ...view,
      latest: { ...view.latest!, status: 'Pending', whiteBalls: null, special: null },
    });

    expect(el.querySelector('.latest .muted')?.textContent).toContain('pending');
    expect(el.querySelector('app-number-balls')).toBeNull();
  });

  it('reports a jackpot that rolled over', async () => {
    const view = loaded();
    const el = await render({
      ...view,
      latest: { ...view.latest!, jackpotAmount: 500_000_000, jackpotWon: false },
    });

    expect(el.querySelector('.latest-head')?.textContent).toContain('$500 million');
    expect(el.querySelector('.latest-head')?.textContent).toContain('rolled over');
  });

  it('reports a jackpot that was won', async () => {
    const view = loaded();
    const el = await render({
      ...view,
      latest: { ...view.latest!, jackpotAmount: 500_000_000, jackpotWon: true },
    });

    expect(el.querySelector('.latest-head')?.textContent).toContain('jackpot won');
  });
});
