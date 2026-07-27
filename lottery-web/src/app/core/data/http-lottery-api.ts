import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Game } from '../domain/game';
import {
  ApiUnreachableError,
  CheckResultDto,
  GeneratedPicksDto,
  LatestDrawDto,
  LotteryApi,
  NextDrawDto,
  RuleEraDto,
} from '../ports/lottery-api';

/** HTTP adapter for the LotteryApi port. Same-origin /api/* (dev proxy / SWA linked backend). */
@Injectable()
export class HttpLotteryApi extends LotteryApi {
  private readonly http = inject(HttpClient);

  override nextDraw(game: Game): Promise<NextDrawDto> {
    return this.get<NextDrawDto>(`/api/${game}/next-draw`);
  }

  override latest(game: Game): Promise<LatestDrawDto> {
    return this.get<LatestDrawDto>(`/api/${game}/latest`);
  }

  override ruleEras(game: Game): Promise<RuleEraDto[]> {
    return this.get<RuleEraDto[]>(`/api/${game}/rule-eras`);
  }

  override generate(game: Game): Promise<GeneratedPicksDto> {
    return this.get<GeneratedPicksDto>(`/api/${game}/generate`);
  }

  override check(game: Game, whites: number[], special: number): Promise<CheckResultDto> {
    return this.get<CheckResultDto>(`/api/${game}/check?whites=${whites.join(',')}&special=${special}`);
  }

  private async get<T>(url: string): Promise<T> {
    try {
      return await firstValueFrom(this.http.get<T>(url));
    } catch (e) {
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
