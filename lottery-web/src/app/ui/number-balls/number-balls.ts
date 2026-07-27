import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Dumb display of five white balls + the coloured special ball. */
@Component({
  selector: 'app-number-balls',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (n of whites(); track $index) {
      <span class="ball">{{ n }}</span>
    }
    <span class="ball special" [class.pb]="accent() === 'pb'" [class.mm]="accent() === 'mm'">
      {{ special() }}
    </span>
  `,
  styles: `
    :host { display: inline-flex; gap: 0.4rem; }
    .ball {
      width: 2.2rem; height: 2.2rem; border-radius: 50%;
      display: inline-flex; align-items: center; justify-content: center;
      font-weight: 600; font-size: 0.95rem;
      background: var(--ball-bg); border: 1px solid var(--ball-border);
    }
    .special.pb { background: var(--pb-accent); border-color: var(--pb-accent); color: #fff; }
    .special.mm { background: var(--mm-accent); border-color: var(--mm-accent); color: #3a2a00; }
  `,
})
export class NumberBalls {
  readonly whites = input.required<number[]>();
  readonly special = input.required<number>();
  readonly accent = input.required<string>();
}
