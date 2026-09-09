/** Route/API identifier for a game. */
export type Game = 'powerball' | 'megamillions';

export interface GameMeta {
  readonly game: Game;
  readonly name: string;
  readonly specialName: string;
  readonly scheduleLabel: string;
  /** CSS class carrying the game's accent colour (red PB, amber MM). */
  readonly accentClass: string;
}

export const GAMES: readonly GameMeta[] = [
  {
    game: 'powerball',
    name: 'Powerball',
    specialName: 'Powerball',
    scheduleLabel: 'Mon · Wed · Sat',
    accentClass: 'pb',
  },
  {
    game: 'megamillions',
    name: 'Mega Millions',
    specialName: 'Mega Ball',
    scheduleLabel: 'Tue · Fri',
    accentClass: 'mm',
  },
];
