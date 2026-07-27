import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CheckerStore } from './checker-store';
import { FakeLotteryApi } from './fake-lottery-api';
import { LotteryApi } from '../ports/lottery-api';

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

  it('loads the current era on creation', () => {
    expect(store.era()?.whiteBallMax).toBe(69);
  });

  it('rejects out-of-era whites with a reason', () => {
    [1, 2, 3, 4, 70].forEach((v, i) => store.setWhite(i, v));
    store.setSpecial(5);
    expect(store.validationError()).toContain('between 1 and 69');
    expect(store.canCheck()).toBeFalse();
  });

  it('rejects duplicate whites', () => {
    [1, 1, 2, 3, 4].forEach((v, i) => store.setWhite(i, v));
    store.setSpecial(5);
    expect(store.validationError()).toContain('distinct');
  });

  it('stays quiet while the ticket is incomplete', () => {
    store.setWhite(0, 7);
    expect(store.validationError()).toBeNull();
    expect(store.canCheck()).toBeFalse();
  });

  it('checks a valid ticket through the port', async () => {
    [7, 19, 33, 51, 64].forEach((v, i) => store.setWhite(i, v));
    store.setSpecial(18);
    expect(store.canCheck()).toBeTrue();

    await store.check();

    expect(api.checkCalls).toEqual([{ game: 'powerball', whites: [7, 19, 33, 51, 64], special: 18 }]);
    expect(store.result()?.drawsChecked).toBe(1971);
  });

  it('generate fills picks from the port', async () => {
    await store.generate();
    expect(store.whites()).toEqual([7, 19, 33, 51, 64]);
    expect(store.special()).toBe(18);
  });
});
