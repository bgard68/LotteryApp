import { countdownTo, formatCountdown } from './countdown';

describe('countdownTo', () => {
  it('splits remaining time into parts', () => {
    const now = Date.UTC(2026, 6, 27, 16, 0, 0);
    const target = Date.UTC(2026, 6, 28, 2, 59, 0);
    expect(countdownTo(target, now)).toEqual({ days: 0, hours: 10, minutes: 59, seconds: 0 });
  });

  it('carries days for multi-day waits', () => {
    const now = Date.UTC(2026, 6, 27, 0, 0, 0);
    const target = Date.UTC(2026, 6, 29, 3, 0, 30);
    expect(countdownTo(target, now)).toEqual({ days: 2, hours: 3, minutes: 0, seconds: 30 });
  });

  it('clamps at zero once the target has passed', () => {
    expect(countdownTo(1000, 5000)).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 0 });
  });
});

describe('formatCountdown', () => {
  it('formats hh:mm:ss with zero-padding', () => {
    expect(formatCountdown({ days: 0, hours: 4, minutes: 3, seconds: 9 })).toBe('04:03:09');
  });

  it('prefixes days only when present', () => {
    expect(formatCountdown({ days: 1, hours: 4, minutes: 38, seconds: 12 })).toBe('1d 04:38:12');
  });
});
