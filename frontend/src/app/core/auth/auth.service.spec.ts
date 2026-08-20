import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { AuthService } from './auth.service';
import { LoginResponse, RoleName } from '../models/api.models';

const adminSession: LoginResponse = {
  accessToken: 'access-token-value',
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

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentUser()).toBeNull();
    expect(service.accessToken).toBeNull();
  });

  it('holds the session after a successful sign-in', () => {
    service.login('admin', 'Admin@123456').subscribe();

    http.expectOne('/api/auth/login').flush(adminSession);

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken).toBe('access-token-value');
    expect(service.currentUser()?.username).toBe('admin');
    expect(service.isAdmin()).toBe(true);
  });

  /**
   * The security claim, asserted rather than asserted-in-prose: if a token ever reaches web storage, an XSS
   * foothold can read it out later. This test is the reason that cannot regress quietly.
   */
  it('never writes the token to local or session storage', () => {
    service.login('admin', 'Admin@123456').subscribe();
    http.expectOne('/api/auth/login').flush(adminSession);

    const stored = [
      ...Object.keys(localStorage).map((key) => localStorage.getItem(key)),
      ...Object.keys(sessionStorage).map((key) => sessionStorage.getItem(key)),
    ].join('|');

    expect(stored).not.toContain('access-token-value');
    expect(Object.keys(localStorage)).not.toContain('accessToken');
    expect(Object.keys(sessionStorage)).not.toContain('accessToken');
  });

  it('sends credentials on sign-in and refresh so the httpOnly cookie is exchanged', () => {
    service.login('admin', 'Admin@123456').subscribe();
    expect(http.expectOne('/api/auth/login').request.withCredentials).toBe(true);

    service.refresh().subscribe();
    expect(http.expectOne('/api/auth/refresh').request.withCredentials).toBe(true);
  });

  it('clears the session on sign-out', () => {
    service.applySession(adminSession);

    service.logout().subscribe();
    http.expectOne('/api/auth/logout').flush(null);

    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentUser()).toBeNull();
  });

  it('restores a session from the refresh cookie at bootstrap', async () => {
    const restore = service.restoreSession();

    http.expectOne('/api/auth/refresh').flush(adminSession);
    await restore;

    expect(service.isAuthenticated()).toBe(true);
    expect(service.initialised()).toBe(true);
  });

  it('settles quietly when there is no session to restore', async () => {
    const restore = service.restoreSession();

    // The normal case for a first-time visitor: a failure here must not surface as an error.
    http.expectOne('/api/auth/refresh').flush(
      { errorCode: 'INVALID_CREDENTIALS' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await restore;

    expect(service.isAuthenticated()).toBe(false);
    expect(service.initialised()).toBe(true);
  });

  it('reports roles through hasRole', () => {
    service.applySession(adminSession);

    expect(service.hasRole(RoleName.admin)).toBe(true);
    expect(service.hasRole(RoleName.user, RoleName.readOnly)).toBe(false);
  });
});
