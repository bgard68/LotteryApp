import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Game } from '../domain/game';
import {
  ApiUnreachableError,
  RateLimitedError,
  CheckResultDto,
  GeneratedPicksDto,
  LatestDrawDto,
  LotteryApi,
  NextDrawDto,
  RuleEraDto,
} from '../ports/lottery-api';
import { API_BASE_URL } from '../ports/api-base-url';

/**
 * HTTP adapter for the LotteryApi port. Requests go to `{base}/api/*`, where
 * base is empty in development (the dev proxy makes it same-origin) and the
 * App Service origin in production - the free Static Web Apps SKU cannot proxy
 * to a backend, so the browser calls the API directly.
 */
@Injectable()
export class HttpLotteryApi extends LotteryApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  override nextDraw(game: Game): Promise<NextDrawDto> {
    return this.get<NextDrawDto>(`/api/${segment(game)}/next-draw`);
  }

  override latest(game: Game): Promise<LatestDrawDto> {
    return this.get<LatestDrawDto>(`/api/${segment(game)}/latest`);
  }

  override ruleEras(game: Game): Promise<RuleEraDto[]> {
    return this.get<RuleEraDto[]>(`/api/${segment(game)}/rule-eras`);
  }

  override generate(game: Game, count: number): Promise<GeneratedPicksDto> {
    return this.get<GeneratedPicksDto>(
      `/api/${segment(game)}/generate`,
      new HttpParams().set('count', count));
  }

  override check(game: Game, whites: number[], special: number): Promise<CheckResultDto> {
    return this.get<CheckResultDto>(
      `/api/${segment(game)}/check`,
      new HttpParams().set('whites', whites.join(',')).set('special', special));
  }

  private async get<T>(path: string, params?: HttpParams): Promise<T> {
    try {
      return await firstValueFrom(this.http.get<T>(`${this.base}${path}`, { params }));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 429)
        throw new RateLimitedError();
      if (e instanceof HttpErrorResponse && isUnreachable(e))
        throw new ApiUnreachableError();
      throw e;
    }
  }
}

/**
 * The game is the only path segment built from a value that reaches this layer
 * through a DOM escape hatch - the checker reads it via `$any($event.target)`,
 * which TypeScript cannot police. Today the <select> options constrain it, but
 * nothing at RUNTIME does, and a path segment is the one place where an
 * unencoded value can change which endpoint is called rather than merely what
 * is asked of it.
 *
 * Query values go through HttpParams for the same reason: encoding by
 * construction beats remembering to encode.
 */
function segment(value: string): string {
  return encodeURIComponent(value);
}

/**
 * Status 0 = network failure; 502/503/504 = a gateway (SWA linked backend)
 * answered but the API behind it did not; 500 with an EMPTY body is Vite's
 * dev proxy reporting ECONNREFUSED - a real API 500 always carries a body.
 */
function isUnreachable(e: HttpErrorResponse): boolean {
  if ([0, 502, 503, 504].includes(e.status)) return true;
  return e.status === 500 && (e.error == null || e.error === '');
}
