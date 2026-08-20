import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { apiErrorInterceptor } from '../http/api-error.interceptor';
import { acceptLanguageInterceptor } from './accept-language.interceptor';
import { authRefreshInterceptor, resetRefreshStateForTests } from './auth-refresh.interceptor';
import { authTokenInterceptor } from './auth-token.interceptor';
import { correlationIdInterceptor } from './correlation-id.interceptor';
import { LocaleService } from '../services/locale.service';
import { LoginResponse, RoleName } from '../models/api.models';

/** Transloco needs a loader even when nothing is translated; LocaleService depends on the service. */
class StubLoader {
  getTranslation(): Observable<Record<string, string>> {
    return of({});
  }
}

const session: LoginResponse = {
  accessToken: 'token-1',
  expiresAt: new Date(Date.now() + 900_000).toISOString(),
  user: {
    id: 'user-1',
    username: 'admin',
    email: 'admin@example.com',
    firstName: 'System',
    lastName: 'Administrator',
    role: RoleName.admin,
  },
};

describe('authTokenInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authTokenInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  it('omits the header when there is no session', () => {
    http.get('/api/users').subscribe();

    expect(controller.expectOne('/api/users').request.headers.has('Authorization')).toBe(false);
  });

  it('attaches the bearer token when signed in', () => {
    auth.applySession(session);
    http.get('/api/users').subscribe();

    expect(controller.expectOne('/api/users').request.headers.get('Authorization')).toBe('Bearer token-1');
  });

  it.each(['/api/auth/login', '/api/auth/refresh'])('never attaches a token to %s', (url) => {
    auth.applySession(session);
    http.post(url, {}).subscribe();

    // Sending an expired token to the refresh endpoint would make the refresh itself fail authentication,
    // turning a recoverable session into a forced sign-out.
    expect(controller.expectOne(url).request.headers.has('Authorization')).toBe(false);
  });
});

describe('correlationIdInterceptor', () => {
  it('tags every request with a unique correlation id', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationIdInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);

    http.get('/api/users').subscribe();
    const first = controller.expectOne('/api/users').request.headers.get('X-Correlation-Id');

    http.get('/api/roles').subscribe();
    const second = controller.expectOne('/api/roles').request.headers.get('X-Correlation-Id');

    expect(first).toBeTruthy();
    expect(second).toBeTruthy();
    expect(first).not.toBe(second);
  });
});

describe('acceptLanguageInterceptor', () => {
  it('asks the API for the language the UI is showing', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([acceptLanguageInterceptor])),
        provideHttpClientTesting(),
        provideTransloco({
          config: { availableLangs: ['en', 'ar'], defaultLang: 'en' },
          loader: StubLoader,
        }),
      ],
    });

    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);
    TestBed.inject(LocaleService).setLocale('ar');

    http.get('/api/users').subscribe();

    // Without this the interface would be Arabic while its error messages stayed English.
    expect(controller.expectOne('/api/users').request.headers.get('Accept-Language')).toBe('ar');
  });
});

describe('authRefreshInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    resetRefreshStateForTests();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'login', children: [] }]),
        // Same order as app.config.ts. Responses travel back through the array in reverse, so apiError must be
        // last to map before authRefresh reads the error.
        provideHttpClient(withInterceptors([authTokenInterceptor, authRefreshInterceptor, apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.applySession(session);
  });

  it('refreshes once and replays the request', async () => {
    const result = new Promise((resolve) => http.get('/api/users').subscribe({ next: resolve }));

    controller
      .expectOne('/api/users')
      .flush({ errorCode: 'UNAUTHENTICATED' }, { status: 401, statusText: 'Unauthorized' });

    controller.expectOne('/api/auth/refresh').flush({ ...session, accessToken: 'token-2' });

    const replay = controller.expectOne('/api/users');

    // Replayed with the new token, not the stale one that caused the 401.
    expect(replay.request.headers.get('Authorization')).toBe('Bearer token-2');
    replay.flush({ items: [] });

    await expect(result).resolves.toEqual({ items: [] });
  });

  /**
   * The case that matters most. Refresh tokens rotate, so a second concurrent refresh is a replay of an
   * already-used token - which the server treats as theft and answers by revoking the whole family. One
   * expired token must therefore produce exactly one refresh.
   */
  it('issues a single refresh for several concurrent 401s', async () => {
    const first = new Promise((resolve) => http.get('/api/users').subscribe({ next: resolve }));
    const second = new Promise((resolve) => http.get('/api/roles').subscribe({ next: resolve }));

    controller
      .expectOne('/api/users')
      .flush({ errorCode: 'UNAUTHENTICATED' }, { status: 401, statusText: 'Unauthorized' });
    controller
      .expectOne('/api/roles')
      .flush({ errorCode: 'UNAUTHENTICATED' }, { status: 401, statusText: 'Unauthorized' });

    // Exactly one: expectOne throws if the interceptor fired a second refresh.
    controller.expectOne('/api/auth/refresh').flush({ ...session, accessToken: 'token-2' });

    controller.expectOne('/api/users').flush({ items: [] });
    controller.expectOne('/api/roles').flush([]);

    await expect(first).resolves.toEqual({ items: [] });
    await expect(second).resolves.toEqual([]);
  });

  it('gives up and clears the session when the refresh fails', async () => {
    const failure = new Promise((resolve) => http.get('/api/users').subscribe({ error: resolve }));

    controller
      .expectOne('/api/users')
      .flush({ errorCode: 'UNAUTHENTICATED' }, { status: 401, statusText: 'Unauthorized' });
    controller
      .expectOne('/api/auth/refresh')
      .flush({ errorCode: 'INVALID_CREDENTIALS' }, { status: 401, statusText: 'Unauthorized' });

    await failure;

    expect(auth.isAuthenticated()).toBe(false);
  });

  it.each(['INVALID_CREDENTIALS', 'ACCOUNT_LOCKED'])(
    'does not try to refresh after a %s response',
    (code) => {
      let received: unknown;

      http.post('/api/users/me/change-password', {}).subscribe({ error: (error) => (received = error) });

      controller
        .expectOne('/api/users/me/change-password')
        .flush({ errorCode: code }, { status: 401, statusText: 'Unauthorized' });

      // Synchronous: HttpTestingController delivers the response during flush, so there is nothing to await -
      // and awaiting a promise that was already settled inside flush is what made the earlier version of this
      // test hang.
      expect(received).toMatchObject({ code });

      // Neither is an expired-token problem, so refreshing would be pointless traffic.
      controller.expectNone('/api/auth/refresh');
    },
  );
});




