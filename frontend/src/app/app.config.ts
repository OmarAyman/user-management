import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, TitleStrategy, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';

import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { TranslatedTitleStrategy } from './core/i18n/translated-title.strategy';
import { acceptLanguageInterceptor } from './core/interceptors/accept-language.interceptor';
import { authRefreshInterceptor } from './core/interceptors/auth-refresh.interceptor';
import { authTokenInterceptor } from './core/interceptors/auth-token.interceptor';
import { correlationIdInterceptor } from './core/interceptors/correlation-id.interceptor';
import { apiErrorInterceptor } from './core/http/api-error.interceptor';
import { LocaleService } from './core/services/locale.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    provideRouter(
      routes,
      // Query params bind straight to component inputs, which is what lets the user list keep its state in the
      // URL without a component reading the router itself.
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),

    // Route titles are translation keys, not text: the tab title is user-visible, so it follows the same
    // localization rule as everything inside a template.
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },

    // Interceptor order is behaviour, and the two halves of the exchange run in opposite directions:
    // requests pass through this array top to bottom, responses come back bottom to top.
    //
    //   correlationId  - request side, tags everything including the calls below
    //   acceptLanguage - request side, so the server answers in the UI's language
    //   authToken      - request side, attaches the bearer token
    //   authRefresh    - response side, and therefore listed BEFORE the mapper
    //   apiError       - response side, closest to the backend, so it maps first
    //
    // That last pair is the subtle one. Being last in the array puts apiError nearest the network, so on the
    // way back it converts HttpErrorResponse into the typed ApiError before authRefresh ever sees it - which
    // is what lets the refresh logic branch on a discriminated union instead of re-reading status codes.
    //
    // The reverse arrangement compiles, runs, and is wrong: authRefresh receives the raw error, falls into its
    // defensive branch, and tries to refresh after a genuinely bad password. A test caught exactly that.
    provideHttpClient(
      withInterceptors([
        correlationIdInterceptor,
        acceptLanguageInterceptor,
        authTokenInterceptor,
        authRefreshInterceptor,
        apiErrorInterceptor,
      ]),
    ),

    provideTransloco({
      config: {
        availableLangs: ['en', 'ar'],
        defaultLang: 'en',
        reRenderOnLangChange: true,
        prodMode: false,
        missingHandler: {
          // Falls back rather than rendering a raw key: a missing translation should look like plain English,
          // not like a bug in a dialog.
          useFallbackTranslation: true,
          allowEmpty: false,
        },
      },
      loader: TranslocoHttpLoader,
    }),

    // Sets language and direction before the first paint, so an Arabic user never sees a left-to-right flash.
    provideAppInitializer(() => {
      inject(LocaleService).initialise();
    }),

    // Exchanges the httpOnly refresh cookie for an access token before routing, so a reload on a protected
    // page stays on that page instead of bouncing through the sign-in screen.
    provideAppInitializer(() => inject(AuthService).restoreSession()),
  ],
};
