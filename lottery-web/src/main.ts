import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { API_BASE_URL, loadRuntimeConfig } from './app/core/ports/api-base-url';

// The API origin is resolved before bootstrap so it is a plain value the whole
// app can inject, rather than an async lookup every adapter has to await.
// Empty (same origin) locally; the deployed App Service URL in Azure.
loadRuntimeConfig()
  .then((apiBaseUrl) =>
    bootstrapApplication(App, {
      ...appConfig,
      providers: [...appConfig.providers, { provide: API_BASE_URL, useValue: apiBaseUrl }],
    }),
  )
  .catch((err) => console.error(err));
