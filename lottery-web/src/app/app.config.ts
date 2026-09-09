import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpLotteryApi } from './core/data/http-lottery-api';
import { LotteryApi } from './core/ports/lottery-api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(),
    // Depend on the abstraction; bind the concrete HTTP adapter here (DIP).
    { provide: LotteryApi, useClass: HttpLotteryApi },
  ],
};
