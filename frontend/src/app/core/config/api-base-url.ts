import { InjectionToken } from '@angular/core';

/**
 * Where the API lives.
 *
 * A token rather than a hard-coded string or an `environment.ts` import, so a test can supply its own value
 * without touching a build configuration, and a deployment can override it at bootstrap.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '/api',
});
