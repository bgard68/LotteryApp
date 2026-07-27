import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { FakeLotteryApi } from './core/state/fake-lottery-api';
import { LotteryApi } from './core/ports/lottery-api';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        { provide: LotteryApi, useValue: new FakeLotteryApi() },
      ],
    }).compileComponents();
  });

  it('renders the dashboard shell with both game cards and the footer disclaimer', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('h1')?.textContent).toContain('Lucky numbers');
    expect(el.querySelectorAll('app-game-card').length).toBe(2);
    expect(el.querySelector('app-ticket-checker')).toBeTruthy();
    expect(el.querySelector('footer')?.textContent).toContain('not affiliated');
  });
});
