import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import { AuthenticatedUser, LoginResponse, RoleName } from '../models/api.models';

/**
 * The session.
 *
 * The access token lives in a signal and nowhere else - not `localStorage`, not `sessionStorage`, not a
 * non-httpOnly cookie. An XSS foothold therefore cannot read it out of storage after the fact; it would have to
 * be active in the page at the time. The trade-off is that a page reload loses the token, which is exactly what
 * the httpOnly refresh cookie exists to solve: `restoreSession()` exchanges it for a new access token at
 * bootstrap (ADR-0005).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly currentUserSignal = signal<AuthenticatedUser | null>(null);

  /** True once the initial refresh attempt has settled, so guards do not run against an unknown state. */
  private readonly initialisedSignal = signal(false);

  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly initialised = this.initialisedSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);
  readonly role = computed(() => this.currentUserSignal()?.role ?? null);
  readonly isAdmin = computed(() => this.role() === RoleName.admin);

  /** Read by the token interceptor. Deliberately not exposed as a signal to discourage storing it anywhere. */
  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/auth/login`, { username, password }, { withCredentials: true })
      .pipe(tap((response) => this.applySession(response)));
  }

  /**
   * Exchanges the refresh cookie for a new access token.
   *
   * `withCredentials` is required on this call and on sign-in: without it the browser neither stores nor sends
   * the cookie, and every reload would look like a sign-out.
   */
  refresh(): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/auth/refresh`, {}, { withCredentials: true })
      .pipe(tap((response) => this.applySession(response)));
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(`${this.baseUrl}/auth/logout`, {}, { withCredentials: true })
      .pipe(tap(() => this.clearSession()));
  }

  /**
   * Called once at bootstrap. A failure here is the normal case for a first-time visitor, so it resolves
   * quietly rather than surfacing an error.
   */
  restoreSession(): Promise<void> {
    return new Promise((resolve) => {
      this.refresh().subscribe({
        next: () => {
          this.initialisedSignal.set(true);
          resolve();
        },
        error: () => {
          this.clearSession();
          this.initialisedSignal.set(true);
          resolve();
        },
      });
    });
  }

  applySession(response: LoginResponse): void {
    this.accessTokenSignal.set(response.accessToken);
    this.currentUserSignal.set(response.user);
  }

  clearSession(): void {
    this.accessTokenSignal.set(null);
    this.currentUserSignal.set(null);
  }

  hasRole(...roles: readonly string[]): boolean {
    const current = this.role();

    return current !== null && roles.includes(current);
  }
}
