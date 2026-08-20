import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

/**
 * Requires a session.
 *
 * UX only. Every route it protects is also protected server-side, and the integration suite proves the API
 * refuses the same operations this guard hides. A guard that was the only check would be no check at all.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  // returnUrl so a deep link survives sign-in: sending everyone to the dashboard after authenticating loses
  // the page they actually asked for.
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Requires one of the given roles, declared on the route.
 *
 * Sends a signed-in user without the role to an explicit /forbidden page rather than redirecting them home:
 * a silent redirect makes a permissions problem look like a broken link.
 */
export function roleGuard(...roles: readonly string[]): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return auth.hasRole(...roles) ? true : router.createUrlTree(['/forbidden']);
  };
}
