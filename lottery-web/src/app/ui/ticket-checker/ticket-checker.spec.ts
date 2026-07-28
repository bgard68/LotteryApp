import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TicketChecker } from './ticket-checker';
import { CheckerStore } from '../../core/state/checker-store';
import { FakeLotteryApi } from '../../core/state/fake-lottery-api';
import { LotteryApi } from '../../core/ports/lottery-api';

/**
 * The results legend: the balls carry meaning in colour alone, so the key that
 * decodes them has to be present - and has to name the right special ball.
 */
describe('TicketChecker results legend', () => {
  let api: FakeLotteryApi;

  async function render(game: 'powerball' | 'megamillions', withResults: boolean) {
    api = new FakeLotteryApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: api },
      ],
    });

    const store = TestBed.inject(CheckerStore);
    await store.setGame(game);

    const fixture = TestBed.createComponent(TicketChecker);
    await fixture.whenStable();
    fixture.detectChanges();

    if (withResults) {
      store.tickets.set([{ whites: [1, 2, 3, 4, 5], special: 6, selected: true }]);
      await store.check();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('is absent until a check has run', async () => {
    const el = await render('powerball', false);

    expect(el.querySelector('.legend')).toBeNull();
  });

  it('appears with the results and explains both colours', async () => {
    const el = await render('powerball', true);
    const legend = el.querySelector('.legend');

    expect(legend).toBeTruthy();
    expect(legend!.textContent).toContain('a number on your ticket');
    expect(legend!.querySelector('.swatch.hit')).toBeTruthy();
    expect(legend!.querySelector('.swatch.special')).toBeTruthy();
  });

  it('names the Powerball and tints its swatch red', async () => {
    const el = await render('powerball', true);
    const legend = el.querySelector('.legend')!;

    expect(legend.textContent).toContain('the Powerball');
    expect(legend.querySelector('.swatch.special.pb')).toBeTruthy();
    expect(legend.querySelector('.swatch.special.mm')).toBeNull();
  });

  it('names the Mega Ball and tints its swatch gold', async () => {
    const el = await render('megamillions', true);
    const legend = el.querySelector('.legend')!;

    expect(legend.textContent).toContain('the Mega Ball');
    expect(legend.querySelector('.swatch.special.mm')).toBeTruthy();
    expect(legend.querySelector('.swatch.special.pb')).toBeNull();
  });

  it('leaves the prize disclaimer in place below the rows', async () => {
    const el = await render('powerball', true);
    const hint = el.querySelector('.results .hint');

    expect(hint!.textContent).toContain('approximate tier values');
    // The sentence the legend replaced must not linger in both places.
    expect(hint!.textContent).not.toContain('flashing');
  });
});
