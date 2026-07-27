import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CheckerStore } from './checker-store';
import { FakeLotteryApi } from './fake-lottery-api';
import { ApiUnreachableError, LotteryApi } from '../ports/lottery-api';

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

  it('an unreachable backend produces an actionable message', async () => {
    api.generateError = new ApiUnreachableError();
    await store.generate();
    expect(store.error()).toContain('start the backend');
  });

  it('other failures keep the generic message', async () => {
    api.generateError = new Error('boom');
    await store.generate();
    expect(store.error()).toContain('Something went wrong');
  });
});
