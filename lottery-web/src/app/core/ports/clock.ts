import { InjectionToken } from '@angular/core';

/**
 * The frontend's TimeProvider: stores read "now" through this token, so tests
 * freeze and advance time instead of racing real timers.
 */
export const CLOCK = new InjectionToken<() => number>('CLOCK', {
  providedIn: 'root',
  factory: () => () => Date.now(),
});
