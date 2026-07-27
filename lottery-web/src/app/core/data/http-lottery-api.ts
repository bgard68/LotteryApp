import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Game } from '../domain/game';
import {
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
    return firstValueFrom(this.http.get<NextDrawDto>(`/api/${game}/next-draw`));
  }

  override latest(game: Game): Promise<LatestDrawDto> {
    return firstValueFrom(this.http.get<LatestDrawDto>(`/api/${game}/latest`));
  }

  override ruleEras(game: Game): Promise<RuleEraDto[]> {
    return firstValueFrom(this.http.get<RuleEraDto[]>(`/api/${game}/rule-eras`));
  }

  override generate(game: Game): Promise<GeneratedPicksDto> {
    return firstValueFrom(this.http.get<GeneratedPicksDto>(`/api/${game}/generate`));
  }

  override check(game: Game, whites: number[], special: number): Promise<CheckResultDto> {
    return firstValueFrom(this.http.get<CheckResultDto>(
      `/api/${game}/check?whites=${whites.join(',')}&special=${special}`));
  }
}
