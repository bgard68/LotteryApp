import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpLotteryApi } from './http-lottery-api';
import { API_BASE_URL } from '../ports/api-base-url';
import { ApiUnreachableError, LotteryApi, RateLimitedError } from '../ports/lottery-api';
import type { Game } from '../domain/game';

/**
 * The one place mocking HTTP is right: this class exists to speak HTTP, so the
 * request it builds IS the behaviour under test. Everywhere else the LotteryApi
 * port is faked instead.
 */
describe('HttpLotteryApi', () => {
  let api: LotteryApi;
  let http: HttpTestingController;

  function configure(base = '') {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: base },
        { provide: LotteryApi, useClass: HttpLotteryApi },
      ],
    });
    api = TestBed.inject(LotteryApi);
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  describe('request shape', () => {
    beforeEach(() => configure());

    it('builds the check request from the ticket', async () => {
      const promise = api.check('powerball', [7, 19, 33, 51, 64], 18);

      const req = http.expectOne((r) => r.url === '/api/powerball/check');
      expect(req.request.params.get('whites')).toBe('7,19,33,51,64');
      expect(req.request.params.get('special')).toBe('18');
      req.flush({ status: 'Ok', drawsChecked: 0, historySince: null, matches: [] });
      await promise;
    });

    it('asks each game for its own next drawing', async () => {
      const promise = api.nextDraw('megamillions');

      const req = http.expectOne('/api/megamillions/next-draw');
      expect(req.request.method).toBe('GET');
      req.flush({
        game: 'megamillions',
        drawDate: '2026-07-28',
        drawTimeUtc: '2026-07-28T02:59:00+00:00',
        estimatedJackpot: 800_000_000,
        cashValue: 344_200_000,
      });

      expect((await promise).estimatedJackpot).toBe(800_000_000);
    });

    it('sends the ticket count as a parameter, not a hand-built string', async () => {
      const promise = api.generate('megamillions', 10);

      const req = http.expectOne((r) => r.url === '/api/megamillions/generate');
      expect(req.request.params.get('count')).toBe('10');
      req.flush({ game: 'megamillions', tickets: [] });
      await promise;
    });

    it('prefixes the configured origin', async () => {
      TestBed.resetTestingModule();
      configure('https://api.example.test');

      const promise = api.latest('powerball');
      const req = http.expectOne('https://api.example.test/api/powerball/latest');
      req.flush({});
      await promise;
    });
  });

  describe('the game reaches this layer through a DOM escape hatch', () => {
    beforeEach(() => configure());

    // `store.setGame($any($event.target).value)` bypasses TypeScript, so nothing
    // at runtime guarantees the game is one of the two known values. A path
    // segment is where that matters: unencoded, it changes WHICH endpoint is
    // called rather than what is asked of it.
    it('encodes a game that tries to escape its path segment', async () => {
      const hostile = '../../internal/refresh?x=' as Game;

      const promise = api.latest(hostile);

      const req = http.expectOne((r) => r.url.startsWith('/api/'));
      expect(req.request.url).toBe('/api/..%2F..%2Finternal%2Frefresh%3Fx%3D/latest');
      expect(req.request.url).not.toContain('/internal/refresh');
      req.flush({});
      await promise;
    });

    it('encodes a game carrying a query separator', async () => {
      const promise = api.ruleEras('powerball&admin=1' as Game);

      const req = http.expectOne((r) => r.url.startsWith('/api/'));
      expect(req.request.url).toBe('/api/powerball%26admin%3D1/rule-eras');
      req.flush([]);
      await promise;
    });
  });

  describe('transport failures are classified, not leaked', () => {
    beforeEach(() => configure());

    it('maps 429 to a rate-limited error', async () => {
      const promise = api.latest('powerball');
      http.expectOne((r) => r.url.startsWith('/api/')).flush('slow down', { status: 429, statusText: 'Too Many Requests' });

      await expectAsync(promise).toBeRejectedWithError(RateLimitedError);
    });

    it('maps a dead backend to an unreachable error', async () => {
      const promise = api.latest('powerball');
      http.expectOne((r) => r.url.startsWith('/api/')).flush('', { status: 503, statusText: 'Service Unavailable' });

      await expectAsync(promise).toBeRejectedWithError(ApiUnreachableError);
    });

    it('leaves a genuine server error alone', async () => {
      const promise = api.latest('powerball');
      http.expectOne((r) => r.url.startsWith('/api/'))
        .flush({ detail: 'boom' }, { status: 500, statusText: 'Server Error' });

      await expectAsync(promise).toBeRejected();
    });
  });
});
