import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { AuthService } from '../auth/auth.service';

/** Paths that must never carry a bearer token: they are how a token is obtained or discarded. */
const ANONYMOUS_PATHS = ['/auth/login', '/auth/refresh'] as const;

/**
 * Attaches the access token.
 *
 * Skips the sign-in and refresh calls deliberately. Sending an expired token to `/auth/refresh` would make the
 * refresh itself fail authentication, turning a recoverable session into a forced sign-out.
 */
export const authTokenInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken;

  if (token === null || ANONYMOUS_PATHS.some((path) => request.url.includes(path))) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
