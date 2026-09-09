import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CheckerStore } from './checker-store';
import { FakeLotteryApi } from './fake-lottery-api';
import { ApiUnreachableError, LotteryApi, RateLimitedError, TicketMatchDto } from '../ports/lottery-api';

describe('CheckerStore', () => {
  let store: CheckerStore;
  let api: FakeLotteryApi;

  beforeEach(async () => {
    api = new FakeLotteryApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: api },
      ],
    });
    store = TestBed.inject(CheckerStore);
    await Promise.resolve(); // let the constructor's era load settle
  });

  function fillTicket(t: number, whites: number[], special: number): void {
    whites.forEach((v, i) => store.setWhite(t, i, v));
    store.setSpecial(t, special);
  }

  it('loads the current era on creation', () => {
    expect(store.era()?.whiteBallMax).toBe(69);
  });

  it('starts with a single empty ticket', () => {
    expect(store.count()).toBe(1);
    expect(store.canCheck()).toBeFalse();
  });

  it('a check with nothing to check never reaches the API', async () => {
    await store.check();

    expect(api.checkCalls).toEqual([]);
    expect(store.results()).toBeNull();
  });

  it('setCount clamps to 1-10 and preserves existing tickets', () => {
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    store.setCount(3);
    expect(store.count()).toBe(3);
    expect(store.tickets()[0].whites).toEqual([7, 19, 33, 51, 64]);

    store.setCount(99);
    expect(store.count()).toBe(10);
    store.setCount(0);
    expect(store.count()).toBe(1);
  });

  it('rejects out-of-era whites naming the ticket', () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [1, 2, 3, 4, 70], 5);
    expect(store.validationError()).toContain('Ticket 2');
    expect(store.validationError()).toContain('between 1 and 69');
    expect(store.canCheck()).toBeFalse();
  });

  it('rejects an out-of-era special ball naming the ticket', () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [1, 2, 3, 4, 5], 27); // the era's special ball stops at 26
    expect(store.validationError()).toContain('Ticket 2');
    expect(store.validationError()).toContain('special ball must be between 1 and 26');
    expect(store.canCheck()).toBeFalse();
  });

  it('rejects duplicate whites', () => {
    fillTicket(0, [1, 1, 2, 3, 4], 5);
    expect(store.validationError()).toContain('distinct');
  });

  it('stays quiet while tickets are incomplete', () => {
    store.setWhite(0, 0, 7);
    expect(store.validationError()).toBeNull();
    expect(store.canCheck()).toBeFalse();
  });

  it('generate fills as many tickets as the count', async () => {
    store.setCount(3);
    await store.generate();
    expect(api.generateCalls).toEqual([{ game: 'powerball', count: 3 }]);
    expect(store.tickets().length).toBe(3);
    expect(store.tickets()[1].whites).toEqual([8, 19, 33, 51, 64]);
    expect(store.canCheck()).toBeTrue();
  });

  it('check runs every ticket through the port and keeps results parallel', async () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [1, 2, 3, 4, 5], 6);

    await store.check();

    expect(api.checkCalls).toEqual([
      { game: 'powerball', whites: [7, 19, 33, 51, 64], special: 18 },
      { game: 'powerball', whites: [1, 2, 3, 4, 5], special: 6 },
    ]);
    expect(store.results()?.length).toBe(2);
  });

  it('checks every complete ticket even when some are unchecked (big wins cover all)', async () => {
    store.setCount(3);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [1, 2, 3, 4, 5], 6);
    fillTicket(2, [10, 20, 30, 40, 50], 7);
    store.toggleSelected(0);
    store.toggleSelected(2); // only ticket 2 remains checkmarked

    await store.check();

    expect(api.checkCalls.length).toBe(3);
    expect(store.results()!.every((r) => r !== null)).toBeTrue();
  });

  it('a checkmarked ticket only needs ITSELF complete; incomplete tickets are skipped', async () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    // ticket 2 left empty and unchecked - checking must still be allowed
    store.toggleSelected(1);
    expect(store.canCheck()).toBeTrue();

    await store.check();

    expect(api.checkCalls).toEqual([{ game: 'powerball', whites: [7, 19, 33, 51, 64], special: 18 }]);
    expect(store.results()![1]).toBeNull();
  });

  // Validation only speaks for the CHECKMARKED tickets, so an unselected one
  // can be complete and still illegal - it must be dropped here rather than
  // sent for the API to reject.
  it('skips an unselected ticket whose numbers repeat', async () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [5, 5, 6, 7, 8], 9);
    store.toggleSelected(1);
    expect(store.canCheck()).toBeTrue();

    await store.check();

    expect(api.checkCalls).toEqual([{ game: 'powerball', whites: [7, 19, 33, 51, 64], special: 18 }]);
    expect(store.results()![1]).toBeNull();
  });

  it('toggling a checkbox keeps existing results (view filter only)', async () => {
    store.setCount(2);
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    fillTicket(1, [1, 2, 3, 4, 5], 6);
    await store.check();

    store.toggleSelected(0);

    expect(store.results()).not.toBeNull();
    expect(store.isSelected(0)).toBeFalse();
    expect(store.isSelected(1)).toBeTrue();
  });

  it('unchecking every ticket disables Check history', () => {
    fillTicket(0, [7, 19, 33, 51, 64], 18);
    store.toggleSelected(0);
    expect(store.canCheck()).toBeFalse();
  });

  it('reports no selection for a ticket the count has since dropped', () => {
    store.setCount(3);
    store.setCount(1);
    expect(store.isSelected(2)).toBeFalse();
  });

  it('pageSize defaults to 10 and accepts all', () => {
    expect(store.pageSize()).toBe(10);
    store.setPageSize('all');
    expect(store.pageSize()).toBe('all');
    store.setPageSize(50);
    expect(store.pageSize()).toBe(50);
  });

  it('an unreachable backend produces an actionable message', async () => {
    api.generateError = new ApiUnreachableError();
    await store.generate();
    expect(store.error()).toContain('start the backend');
  });

  it('a rate-limited request gets a wait-and-retry message', async () => {
    api.generateError = new RateLimitedError();
    await store.generate();
    expect(store.error()).toContain('wait a few seconds');
  });

  it('other failures keep the generic message', async () => {
    api.generateError = new Error('boom');
    await store.generate();
    expect(store.error()).toContain('Something went wrong');
  });

  // The era drives client-side validation, so what happens when it is UNKNOWN
  // matters: the server validates authoritatively, and a rules lookup that
  // failed must not lock the user out of checking.
  describe('when the rule eras are unknown', () => {
    it('drops the old game\'s rules when the new game\'s lookup fails', async () => {
      expect(store.era()?.whiteBallMax).toBe(69);
      spyOn(api, 'ruleEras').and.rejectWith(new ApiUnreachableError());

      await store.setGame('megamillions');

      expect(store.era()).toBeNull();
    });

    it('treats an era list with no current era as unknown', async () => {
      spyOn(api, 'ruleEras').and.resolveTo([
        { effectiveFrom: '2013-10-19', whiteBallMax: 59, whiteBallCount: 5, specialBallMax: 35, isCurrent: false },
      ]);

      await store.setGame('megamillions');

      expect(store.era()).toBeNull();
    });

    it('still lets the ticket be checked - the server has the last word', async () => {
      spyOn(api, 'ruleEras').and.rejectWith(new ApiUnreachableError());
      await store.setGame('megamillions');

      fillTicket(0, [1, 2, 3, 4, 99], 99); // impossible under any real era
      expect(store.validationError()).toBeNull();
      expect(store.canCheck()).toBeTrue();

      await store.check();

      expect(api.checkCalls).toEqual([{ game: 'megamillions', whites: [1, 2, 3, 4, 99], special: 99 }]);
    });
  });

  // The big-win threshold is a rule, and two consumers read it: the highlighted
  // panel and the mobile tab badge. Boundary cases only - "3 or more whites AND
  // the special" has three distinct ways to be wrong.
  describe('bigWins threshold', () => {
    function match(whiteMatches: number, specialMatched: boolean): TicketMatchDto {
      return {
        drawDate: '2026-07-25',
        whiteMatches,
        specialMatched,
        drawnWhiteBalls: [3, 4, 24, 36, 47],
        drawnSpecial: 17,
        tier: `Match ${whiteMatches}${specialMatched ? ' + PB' : ''}`,
        approximateAmount: 100,
        isJackpot: false,
      };
    }

    function resultWith(matches: TicketMatchDto[]) {
      return { status: 'Ok', drawsChecked: 1971, historySince: '2010-02-03', matches };
    }

    async function checkWith(matches: TicketMatchDto[]): Promise<void> {
      api.checkResult = resultWith(matches);
      fillTicket(0, [7, 19, 33, 51, 64], 18);
      await store.check();
    }

    it('is empty before any check has run', () => {
      expect(store.bigWins()).toEqual([]);
    });

    it('counts 3 whites plus the special - the exact boundary', async () => {
      await checkWith([match(3, true)]);
      expect(store.bigWins().length).toBe(1);
    });

    it('rejects 2 whites plus the special (one short)', async () => {
      await checkWith([match(2, true)]);
      expect(store.bigWins()).toEqual([]);
    });

    it('rejects 3 whites without the special', async () => {
      await checkWith([match(3, false)]);
      expect(store.bigWins()).toEqual([]);
    });

    it('rejects 5 whites without the special - white count alone never qualifies', async () => {
      await checkWith([match(5, false)]);
      expect(store.bigWins()).toEqual([]);
    });

    it('accepts 4 and 5 whites plus the special', async () => {
      await checkWith([match(4, true), match(5, true)]);
      expect(store.bigWins().length).toBe(2);
    });

    it('keeps only the qualifying matches out of a mixed result', async () => {
      await checkWith([match(0, true), match(2, true), match(3, true), match(3, false), match(5, true)]);
      expect(store.bigWins().map((w) => w.match.whiteMatches)).toEqual([3, 5]);
    });

    it('carries the ticket index and its draft so the row can be highlighted', async () => {
      await checkWith([match(3, true)]);
      const win = store.bigWins()[0];
      expect(win.ticket).toBe(0);
      expect(win.draft.whites).toEqual([7, 19, 33, 51, 64]);
    });

    it('covers unchecked tickets too - a win must never hide behind a checkbox', async () => {
      store.setCount(2);
      fillTicket(0, [7, 19, 33, 51, 64], 18);
      fillTicket(1, [2, 14, 22, 38, 51], 12);
      store.toggleSelected(1); // ticket 2 unchecked
      api.checkResult = resultWith([match(3, true)]);

      await store.check();

      expect(store.bigWins().map((w) => w.ticket)).toEqual([0, 1]);
    });

    it('clears when the results are discarded by an edit', async () => {
      await checkWith([match(3, true)]);
      expect(store.bigWins().length).toBe(1);

      store.setWhite(0, 0, 8);

      expect(store.bigWins()).toEqual([]);
    });
  });
});
