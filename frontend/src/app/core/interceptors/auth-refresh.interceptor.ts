import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { ApiError, ErrorCode, isApiError } from '../http/api-error.model';

/**
 * Whether a refresh is in flight, and what its outcome was.
 *
 * Module-level rather than per-request: a page that fires several calls at once will see several 401s, and each
 * one must wait on the same refresh instead of starting its own. Without this, one expired token produces N
 * refresh calls, and because refresh tokens rotate, all but the first are replays - which the server correctly
 * treats as token theft and answers by revoking the entire family, signing the user out of everything.
 */
let refreshInFlight = false;
const refreshOutcome = new BehaviorSubject<'pending' | 'succeeded' | 'failed'>('pending');

/**
 * Recovers from an expired access token by refreshing once and replaying the request.
 *
 * Sits downstream of the error mapper, so it branches on a typed discriminant rather than re-reading a status
 * code.
 */
export const authRefreshInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!shouldAttemptRefresh(error, request)) {
        return throwError(() => error);
      }

      if (refreshInFlight) {
        return waitForRefresh(request, next, error);
      }

      refreshInFlight = true;
      refreshOutcome.next('pending');

      return auth.refresh().pipe(
        switchMap(() => {
          refreshInFlight = false;
          refreshOutcome.next('succeeded');

          return next(withFreshToken(request, auth.accessToken));
        }),
        catchError((refreshError: unknown) => {
          refreshInFlight = false;
          refreshOutcome.next('failed');

          // The refresh cookie is gone, expired or revoked: the session is genuinely over.
          auth.clearSession();
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });

          return throwError(() => refreshError);
        }),
      );
    }),
  );
};

function shouldAttemptRefresh(error: unknown, request: HttpRequest<unknown>): boolean {
  if (request.url.includes('/auth/refresh') || request.url.includes('/auth/login')) {
    return false;
  }

  const apiError: ApiError | null = isApiError(error)
    ? error
    : error instanceof HttpErrorResponse && error.status === 401
      ? { kind: 'unauthenticated', code: ErrorCode.unauthenticated, message: error.message }
      : null;

  if (apiError?.kind !== 'unauthenticated') {
    return false;
  }

  // A locked account or wrong credentials are not expired-token problems, and refreshing would not help.
  return apiError.code !== ErrorCode.accountLocked && apiError.code !== ErrorCode.invalidCredentials;
}

/** Queues behind the in-flight refresh, then replays or gives up depending on its outcome. */
function waitForRefresh(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  originalError: unknown,
): Observable<HttpEvent<unknown>> {
  return refreshOutcome.pipe(
    filter((outcome) => outcome !== 'pending'),
    take(1),
    switchMap((outcome) => (outcome === 'succeeded' ? next(request) : throwError(() => originalError))),
  );
}

function withFreshToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token === null ? request : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/** Test hook: the module-level refresh state would otherwise leak between tests. */
export function resetRefreshStateForTests(): void {
  refreshInFlight = false;
  refreshOutcome.next('pending');
}
