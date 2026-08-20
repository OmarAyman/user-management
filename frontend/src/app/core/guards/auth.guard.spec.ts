import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';

import { authGuard, roleGuard } from './auth.guard';
import { AuthService } from '../auth/auth.service';
import { RoleName } from '../models/api.models';

function runGuard(guard: ReturnType<typeof roleGuard> | typeof authGuard, url: string) {
  const state = { url } as RouterStateSnapshot;
  const route = {} as ActivatedRouteSnapshot;

  return TestBed.runInInjectionContext(() => guard(route, state));
}

function sessionFor(role: string) {
  return {
    accessToken: 'token',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 'u1', username: 'someone', email: 'a@b.c', firstName: 'A', lastName: 'B', role },
  };
}

describe('authGuard', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });

    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  it('lets a signed-in caller through', () => {
    auth.applySession(sessionFor(RoleName.user));

    expect(runGuard(authGuard, '/users')).toBe(true);
  });

  it('redirects an anonymous caller to sign-in, keeping the destination', () => {
    const result = runGuard(authGuard, '/users?pageNumber=2');

    expect(result).toBeInstanceOf(UrlTree);

    // The returnUrl is what makes a deep link survive sign-in instead of dumping everyone on the landing page.
    expect(router.serializeUrl(result as UrlTree)).toContain('returnUrl');
  });
});

describe('roleGuard', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });

    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  it('admits a caller holding the required role', () => {
    auth.applySession(sessionFor(RoleName.admin));

    expect(runGuard(roleGuard(RoleName.admin), '/audit')).toBe(true);
  });

  it.each([RoleName.user, RoleName.readOnly])('sends %s to the forbidden page, not home', (role) => {
    auth.applySession(sessionFor(role));

    const result = runGuard(roleGuard(RoleName.admin), '/audit');

    // A silent redirect home would make a permissions problem look like a broken link.
    expect(router.serializeUrl(result as UrlTree)).toContain('/forbidden');
  });

  it('sends an anonymous caller to sign-in rather than to forbidden', () => {
    const result = runGuard(roleGuard(RoleName.admin), '/audit');

    expect(router.serializeUrl(result as UrlTree)).toContain('/login');
  });
});
