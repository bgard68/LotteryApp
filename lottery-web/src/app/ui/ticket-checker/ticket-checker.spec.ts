import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TicketChecker } from './ticket-checker';
import { CheckerStore } from '../../core/state/checker-store';
import { FakeLotteryApi } from '../../core/state/fake-lottery-api';
import { LotteryApi, TicketMatchDto } from '../../core/ports/lottery-api';

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

/**
 * Ticket entry. A `<input type="number">` hands the component a STRING, and
 * what that string means - "" is empty, not zero; "2.5" is not a ball - is the
 * behaviour worth pinning, so these go through the DOM rather than the methods.
 */
describe('TicketChecker ticket entry', () => {
  let fixture: ComponentFixture<TicketChecker>;
  let store: CheckerStore;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: new FakeLotteryApi() },
      ],
    });

    store = TestBed.inject(CheckerStore);
    fixture = TestBed.createComponent(TicketChecker);
    await fixture.whenStable();
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => TestBed.resetTestingModule());

  async function enter(input: HTMLInputElement, value: string, event = 'input'): Promise<void> {
    input.value = value;
    input.dispatchEvent(new Event(event));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function whites(): HTMLInputElement[] {
    return Array.from(el.querySelectorAll<HTMLInputElement>('.ticket-row .inputs input:not(.special)'));
  }

  function special(): HTMLInputElement {
    return el.querySelector<HTMLInputElement>('.ticket-row .inputs input.special')!;
  }

  function checkButton(): HTMLButtonElement {
    return Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent?.includes('Check history')) as HTMLButtonElement;
  }

  async function typeTicket(values: string[], specialValue: string): Promise<void> {
    for (let i = 0; i < values.length; i++) await enter(whites()[i], values[i]);
    await enter(special(), specialValue);
  }

  it('reads a hand-typed ticket into the store and unlocks the check', async () => {
    expect(checkButton().disabled).toBeTrue();

    await typeTicket(['7', '19', '33', '51', '64'], '18');

    expect(store.tickets()[0].whites).toEqual([7, 19, 33, 51, 64]);
    expect(store.tickets()[0].special).toBe(18);
    expect(checkButton().disabled).toBeFalse();
  });

  it('treats a cleared box as empty, never as zero', async () => {
    await typeTicket(['7', '19', '33', '51', '64'], '18');

    await enter(whites()[2], '');

    expect(store.tickets()[0].whites[2]).toBeNull();
    expect(store.validationError()).toBeNull(); // incomplete, not invalid
    expect(checkButton().disabled).toBeTrue();
  });

  it('ignores a fractional entry rather than rounding it into a ball number', async () => {
    await enter(whites()[0], '2.5');

    expect(store.tickets()[0].whites[0]).toBeNull();
  });

  it('clearing the special ball empties it too', async () => {
    await typeTicket(['7', '19', '33', '51', '64'], '18');

    await enter(special(), '');

    expect(store.tickets()[0].special).toBeNull();
    expect(checkButton().disabled).toBeTrue();
  });

  it('renders a row per ticket when more are asked for', async () => {
    const count = el.querySelector<HTMLInputElement>('label.count input')!;

    await enter(count, '3', 'change');

    expect(store.count()).toBe(3);
    expect(el.querySelectorAll('.ticket-row').length).toBe(3);
  });
});

/**
 * What the results panel actually shows: how much of a long history is on
 * screen, and whose numbers are highlighted.
 */
describe('TicketChecker results', () => {
  let fixture: ComponentFixture<TicketChecker>;
  let store: CheckerStore;
  let api: FakeLotteryApi;
  let el: HTMLElement;

  function match(drawDate: string, drawnWhiteBalls: number[], whiteMatches: number, specialMatched = false): TicketMatchDto {
    return {
      drawDate,
      whiteMatches,
      specialMatched,
      drawnWhiteBalls,
      drawnSpecial: 17,
      tier: `Match ${whiteMatches}`,
      approximateAmount: 100,
      isJackpot: false,
    };
  }

  /** Twelve losing drawings - two more than the default page of ten. */
  function twelveMatches(): TicketMatchDto[] {
    return Array.from({ length: 12 }, (_, i) =>
      match(`2026-07-${String(i + 1).padStart(2, '0')}`, [3, 4, 24, 36, 47], 1));
  }

  async function render(matches: TicketMatchDto[], tickets = 1): Promise<void> {
    api = new FakeLotteryApi();
    api.checkResult = { status: 'Ok', drawsChecked: 1971, historySince: '2010-02-03', matches };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: api },
      ],
    });

    store = TestBed.inject(CheckerStore);
    fixture = TestBed.createComponent(TicketChecker);
    await fixture.whenStable();

    store.tickets.set(Array.from({ length: tickets }, () =>
      ({ whites: [3, 4, 60, 61, 62], special: 17, selected: true })));
    await store.check();
    await fixture.whenStable();
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  }

  async function pick(select: HTMLSelectElement, value: string): Promise<void> {
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function pageSize(): HTMLSelectElement {
    return el.querySelector<HTMLSelectElement>('.page-size select')!;
  }

  function truncationHint(): Element | undefined {
    return Array.from(el.querySelectorAll('.ticket-results .small'))
      .find((p) => p.textContent?.includes('Showing first'));
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the first ten of a long history and says how many were held back', async () => {
    await render(twelveMatches());

    expect(el.querySelectorAll('.ticket-results .match').length).toBe(10);
    expect(truncationHint()?.textContent).toContain('Showing first 10 of 12');
  });

  it('shows the whole history once All is picked', async () => {
    await render(twelveMatches());

    await pick(pageSize(), 'all');

    expect(el.querySelectorAll('.ticket-results .match').length).toBe(12);
    expect(truncationHint()).toBeUndefined();
  });

  it('shows everything when the page is bigger than the history', async () => {
    await render(twelveMatches());

    await pick(pageSize(), '25');

    expect(store.pageSize()).toBe(25);
    expect(el.querySelectorAll('.ticket-results .match').length).toBe(12);
    expect(truncationHint()).toBeUndefined();
  });

  it('flashes the drawn numbers that are on your ticket, and no others', async () => {
    await render([match('2026-07-25', [3, 4, 24, 36, 60], 3, true)]);

    const flashing = Array.from(el.querySelectorAll('.big-wins .ball.hit'))
      .map((b) => b.textContent?.trim());

    // The ticket holds 3, 4, 60, 61, 62 and the special 17; 24 and 36 were
    // drawn but are not on it.
    expect(flashing).toEqual(['3', '4', '60', '17']);
  });

  it('unchecking a ticket hides its rows but never its big win', async () => {
    await render([match('2026-07-25', [3, 4, 24, 36, 60], 3, true)], 2);
    expect(el.querySelectorAll('.ticket-results').length).toBe(2);
    expect(el.querySelectorAll('.big-wins .big-row').length).toBe(2);

    el.querySelectorAll<HTMLInputElement>('.ticket-row .pick')[1].click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelectorAll('.ticket-results').length).toBe(1);
    expect(el.querySelectorAll('.big-wins .big-row').length).toBe(2);
    expect(el.textContent).toContain('Ticket 2');
  });
});
