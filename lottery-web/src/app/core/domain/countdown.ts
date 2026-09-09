/** Pure countdown math - given "now" and a target, never reads a clock itself. */
export interface Countdown {
  readonly days: number;
  readonly hours: number;
  readonly minutes: number;
  readonly seconds: number;
}

export function countdownTo(targetUtcMs: number, nowMs: number): Countdown {
  let s = Math.max(0, Math.floor((targetUtcMs - nowMs) / 1000));
  const days = Math.floor(s / 86_400);
  s -= days * 86_400;
  const hours = Math.floor(s / 3600);
  s -= hours * 3600;
  const minutes = Math.floor(s / 60);
  return { days, hours, minutes, seconds: s - minutes * 60 };
}

export function formatCountdown(c: Countdown): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const core = `${pad(c.hours)}:${pad(c.minutes)}:${pad(c.seconds)}`;
  return c.days > 0 ? `${c.days}d ${core}` : core;
}
