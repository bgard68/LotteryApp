import type { Game } from '../domain/game';

/**
 * Thrown by adapters when the backend cannot be reached at all (network
 * failure or dev-proxy 502/503/504), as opposed to the backend answering
 * with an error. Stores use it to show an actionable message instead of a
 * generic one - locally this almost always means "the API isn't running".
 */
export class ApiUnreachableError extends Error {
  constructor() {
    super('The lottery API is unreachable.');
  }
}

/**
 * DTO shapes mirror the backend contract; the generated OpenAPI types in
 * core/api/schema.d.ts are the source-of-truth reference (a CI check keeps
 * them regenerated), and these interfaces are the ergonomic named views the
 * app consumes.
 */
export interface NextDrawDto {
  game: string;
  drawDate: string;
  drawTimeUtc: string;
  estimatedJackpot: number | null;
  cashValue: number | null;
}

export interface LatestDrawDto {
  game: string;
  status: 'Published' | 'Pending';
  drawDate: string;
  whiteBalls: number[] | null;
  special: number | null;
  specialName: string;
  jackpotAmount: number | null;
  jackpotWon: boolean | null;
}

export interface RuleEraDto {
  effectiveFrom: string;
  whiteBallMax: number;
  whiteBallCount: number;
  specialBallMax: number;
  isCurrent: boolean;
}

export interface GeneratedTicketDto {
  whiteBalls: number[];
  special: number;
}

export interface GeneratedPicksDto {
  game: string;
  tickets: GeneratedTicketDto[];
}

export interface TicketMatchDto {
  drawDate: string;
  whiteMatches: number;
  specialMatched: boolean;
  drawnWhiteBalls: number[];
  drawnSpecial: number;
  tier: string;
  approximateAmount: number | null;
  isJackpot: boolean;
}

export interface CheckResultDto {
  status: string;
  drawsChecked: number;
  historySince: string | null;
  matches: TicketMatchDto[];
}

/**
 * The UI's single port to the backend (Dependency Inversion): components and
 * stores depend on this abstraction; the HTTP adapter is bound in app.config.
 */
export abstract class LotteryApi {
  abstract nextDraw(game: Game): Promise<NextDrawDto>;
  abstract latest(game: Game): Promise<LatestDrawDto>;
  abstract ruleEras(game: Game): Promise<RuleEraDto[]>;
  abstract generate(game: Game, count: number): Promise<GeneratedPicksDto>;
  abstract check(game: Game, whites: number[], special: number): Promise<CheckResultDto>;
}
