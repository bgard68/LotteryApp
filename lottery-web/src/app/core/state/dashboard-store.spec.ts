import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DashboardStore } from './dashboard-store';
import { FakeLotteryApi } from './fake-lottery-api';
import { CLOCK } from '../ports/clock';
import { ApiUnreachableError, LotteryApi, RateLimitedError } from '../ports/lottery-api';

/** The drawing the fake API always points at. */
const DRAW_MS = Date.parse('2026-07-28T02:59:00+00:00');

describe('DashboardStore', () => {
  let api: FakeLotteryApi;
  let now: number;

  /** Builds the store at a pinned instant - never at whatever time Karma runs. */
  function createStore(): DashboardStore {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: api },
        { provide: CLOCK, useValue: () => now },
      ],
    });
    return TestBed.inject(DashboardStore);
  }

  /** Lets the in-memory API's already-resolved promises run to completion. */
  async function settle(): Promise<void> {
    for (let i = 0; i < 8; i++) await Promise.resolve();
  }

  beforeEach(() => {
    api = new FakeLotteryApi();
    now = DRAW_MS - 3_600_000; // an hour before the drawing
    jasmine.clock().install();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    TestBed.resetTestingModule();
  });

  it('offers both games as loading before any answer arrives', () => {
    const store = createStore();

    expect(store.cards().map((c) => c.meta.game)).toEqual(['powerball', 'megamillions']);
    expect(store.cards().every((c) => !c.loaded)).toBeTrue();
    expect(store.cards().every((c) => c.next === null && c.latest === null)).toBeTrue();
    expect(store.cards().every((c) => c.countdown === null && c.error === null)).toBeTrue();
  });

  it('fills each card with its own game once the API answers', async () => {
    const store = createStore();

    await settle();

    const [powerball, megamillions] = store.cards();
    expect(powerball.loaded).toBeTrue();
    expect(powerball.next?.game).toBe('powerball');
    expect(powerball.latest?.whiteBalls).toEqual([3, 4, 24, 36, 47]);
    expect(megamillions.next?.estimatedJackpot).toBe(800_000_000);
  });

  it('measures the countdown from the clock, not from wall time', async () => {
    const store = createStore();

    await settle();

    expect(store.cards()[0].countdown).toEqual({ days: 0, hours: 1, minutes: 0, seconds: 0 });
  });

  it('counts down while the page sits open', async () => {
    const store = createStore();
    await settle();

    now += 90_000;
    jasmine.clock().tick(1000);

    expect(store.cards()[0].countdown).toEqual({ days: 0, hours: 0, minutes: 58, seconds: 30 });
  });

  it('refetches after the drawing time so the card flips to Pending on its own', async () => {
    now = DRAW_MS - 5_000;
    const nextDraw = spyOn(api, 'nextDraw').and.callThrough();
    createStore();
    await settle();
    expect(nextDraw).toHaveBeenCalledTimes(2); // one per game

    jasmine.clock().tick(5_000 + 30_000 + 1);
    await settle();

    expect(nextDraw).toHaveBeenCalledTimes(4);
  });

  it('does not schedule a refetch for a drawing that has already passed', async () => {
    now = DRAW_MS + 1;
    const nextDraw = spyOn(api, 'nextDraw').and.callThrough();
    createStore();
    await settle();

    jasmine.clock().tick(24 * 60 * 60 * 1000);
    await settle();

    expect(nextDraw).toHaveBeenCalledTimes(2);
  });

  describe('when a game will not load', () => {
    async function failWith(error: Error): Promise<DashboardStore> {
      spyOn(api, 'nextDraw').and.rejectWith(error);
      const store = createStore();
      await settle();
      return store;
    }

    it('stops saying Loading and shows the failure instead', async () => {
      const store = await failWith(new Error('boom'));

      const card = store.cards()[0];
      expect(card.loaded).toBeTrue();
      expect(card.error).toBe('Could not load drawing data.');
      expect(card.next).toBeNull();
      expect(card.latest).toBeNull();
      expect(card.countdown).toBeNull();
    });

    it('tells a local developer the backend is not running', async () => {
      const store = await failWith(new ApiUnreachableError());

      expect(store.cards()[0].error).toContain('start the backend');
    });

    it('asks a rate-limited visitor to refresh in a moment', async () => {
      const store = await failWith(new RateLimitedError());

      expect(store.cards()[0].error).toContain('refresh again in a few seconds');
    });

    it('clears the message when a later load succeeds', async () => {
      const store = await failWith(new ApiUnreachableError());
      expect(store.cards()[0].error).not.toBeNull();

      (api.nextDraw as jasmine.Spy).and.callThrough();
      await store.load('powerball');

      expect(store.cards()[0].error).toBeNull();
      expect(store.cards()[0].next).not.toBeNull();
      expect(store.cards()[1].error).not.toBeNull(); // the other game is untouched
    });
  });
});
