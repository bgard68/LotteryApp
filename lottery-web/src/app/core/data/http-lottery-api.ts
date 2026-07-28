import { HttpClient, HttpErrorResponse } from '@angular/common/http';
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
    return this.get<NextDrawDto>(`/api/${game}/next-draw`);
  }

  override latest(game: Game): Promise<LatestDrawDto> {
    return this.get<LatestDrawDto>(`/api/${game}/latest`);
  }

  override ruleEras(game: Game): Promise<RuleEraDto[]> {
    return this.get<RuleEraDto[]>(`/api/${game}/rule-eras`);
  }

  override generate(game: Game, count: number): Promise<GeneratedPicksDto> {
    return this.get<GeneratedPicksDto>(`/api/${game}/generate?count=${count}`);
  }

  override check(game: Game, whites: number[], special: number): Promise<CheckResultDto> {
    return this.get<CheckResultDto>(`/api/${game}/check?whites=${whites.join(',')}&special=${special}`);
  }

  private async get<T>(path: string): Promise<T> {
    try {
      return await firstValueFrom(this.http.get<T>(`${this.base}${path}`));
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
 * Status 0 = network failure; 502/503/504 = a gateway (SWA linked backend)
 * answered but the API behind it did not; 500 with an EMPTY body is Vite's
 * dev proxy reporting ECONNREFUSED - a real API 500 always carries a body.
 */
function isUnreachable(e: HttpErrorResponse): boolean {
  if ([0, 502, 503, 504].includes(e.status)) return true;
  return e.status === 500 && (e.error == null || e.error === '');
}
