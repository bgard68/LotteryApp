import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, loadRuntimeConfig } from './api-base-url';

/**
 * The second place mocking the browser is right (HttpLotteryApi is the other):
 * this function exists to fetch `config.json`, so the request it makes and what
 * it does with the answer ARE the behaviour under test.
 *
 * The stakes: this value decides which origin every API call goes to, and it
 * runs once before bootstrap - a wrong answer here breaks the whole app, and a
 * throw here means the app never starts at all.
 */
describe('loadRuntimeConfig', () => {
  function answerWith(body: unknown, ok = true): void {
    spyOn(window, 'fetch').and.resolveTo({
      ok,
      json: () => Promise.resolve(body),
    } as unknown as Response);
  }

  it('takes the origin from config.json', async () => {
    answerWith({ apiBaseUrl: 'https://lottery-api.example.test' });

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('https://lottery-api.example.test');
  });

  it('asks for a fresh copy, so a redeployed config is never served from cache', async () => {
    answerWith({ apiBaseUrl: '' });

    await loadRuntimeConfig();

    expect(window.fetch).toHaveBeenCalledWith('config.json', { cache: 'no-cache' });
  });

  it('trims a trailing slash - paths are appended, and // would 404', async () => {
    answerWith({ apiBaseUrl: 'https://lottery-api.example.test/' });

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('https://lottery-api.example.test');
  });

  it('treats a config without an origin as same-origin', async () => {
    answerWith({});

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('');
  });

  it('treats a missing config.json as same-origin rather than an error', async () => {
    answerWith('Not Found', false);

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('');
  });

  // Static Web Apps rewrites unknown paths to index.html, so a config.json that
  // was never deployed answers 200 with HTML. Bootstrap must survive it.
  it('survives a 200 that is not JSON at all', async () => {
    spyOn(window, 'fetch').and.resolveTo({
      ok: true,
      json: () => Promise.reject(new SyntaxError('Unexpected token <')),
    } as unknown as Response);

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('');
  });

  it('survives a network failure', async () => {
    spyOn(window, 'fetch').and.rejectWith(new TypeError('Failed to fetch'));

    await expectAsync(loadRuntimeConfig()).toBeResolvedTo('');
  });
});

describe('API_BASE_URL', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('defaults to same-origin when bootstrap supplies nothing', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });

    expect(TestBed.inject(API_BASE_URL)).toBe('');
  });
});
