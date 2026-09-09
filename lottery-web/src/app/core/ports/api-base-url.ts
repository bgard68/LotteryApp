import { InjectionToken } from '@angular/core';

/**
 * Origin the API lives on.
 *
 * Empty string in development, where the dev proxy makes `/api/*` same-origin.
 * In production it is the App Service URL, because the free Static Web Apps
 * SKU has no linked-backend proxy (that is a Standard-tier feature), so the
 * browser calls the API's own origin directly - see docs/AZURE-DEPLOYMENT.md.
 *
 * Supplied at runtime from `config.json` rather than baked in at build time,
 * so one build artefact deploys to any environment.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});

export interface RuntimeConfig {
  apiBaseUrl?: string;
}

/**
 * Loads `config.json` before the app starts. A missing or unreadable file is
 * not an error - it simply means "same origin", which is exactly right for
 * local development and for any single-host deployment.
 */
export async function loadRuntimeConfig(): Promise<string> {
  try {
    const response = await fetch('config.json', { cache: 'no-cache' });
    if (!response.ok) return '';
    const config = (await response.json()) as RuntimeConfig;
    return (config.apiBaseUrl ?? '').replace(/\/$/, '');
  } catch {
    return '';
  }
}
