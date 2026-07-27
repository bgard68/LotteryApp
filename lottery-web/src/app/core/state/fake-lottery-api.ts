import type { Game } from '../domain/game';
import {
  CheckResultDto,
  GeneratedPicksDto,
  LatestDrawDto,
  LotteryApi,
  NextDrawDto,
  RuleEraDto,
} from '../ports/lottery-api';

/** In-memory LotteryApi for tests - no HTTP mocking anywhere. */
export class FakeLotteryApi extends LotteryApi {
  checkCalls: { game: Game; whites: number[]; special: number }[] = [];
  checkResult: CheckResultDto = { status: 'Ok', drawsChecked: 1971, historySince: '2010-02-03', matches: [] };

  override nextDraw(game: Game): Promise<NextDrawDto> {
    return Promise.resolve({
      game,
      drawDate: '2026-07-27',
      drawTimeUtc: '2026-07-28T02:59:00+00:00',
      estimatedJackpot: game === 'megamillions' ? 800_000_000 : null,
      cashValue: game === 'megamillions' ? 344_200_000 : null,
    });
  }

  override latest(game: Game): Promise<LatestDrawDto> {
    return Promise.resolve({
      game,
      status: 'Published',
      drawDate: '2026-07-25',
      whiteBalls: [3, 4, 24, 36, 47],
      special: 17,
      specialName: game === 'powerball' ? 'Powerball' : 'Mega Ball',
      jackpotAmount: null,
      jackpotWon: null,
    });
  }

  override ruleEras(_game: Game): Promise<RuleEraDto[]> {
    return Promise.resolve([
      { effectiveFrom: '2015-10-07', whiteBallMax: 69, whiteBallCount: 5, specialBallMax: 26, isCurrent: true },
    ]);
  }

  generateError: Error | null = null;

  override generate(game: Game): Promise<GeneratedPicksDto> {
    if (this.generateError) return Promise.reject(this.generateError);
    return Promise.resolve({ game, whiteBalls: [7, 19, 33, 51, 64], special: 18 });
  }

  override check(game: Game, whites: number[], special: number): Promise<CheckResultDto> {
    this.checkCalls.push({ game, whites, special });
    return Promise.resolve(this.checkResult);
  }
}
