import { signal } from '@angular/core';
import { Viewport } from '../ports/viewport';

/** Test double: specs set the layout explicitly instead of resizing a browser. */
export class FakeViewport implements Viewport {
  private readonly state = signal(false);
  readonly isMobile = this.state.asReadonly();

  constructor(isMobile = false) {
    this.state.set(isMobile);
  }

  set(isMobile: boolean): void {
    this.state.set(isMobile);
  }
}
