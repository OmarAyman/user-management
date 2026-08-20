import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { UsersListPage } from './users-list.page';
import { AuthService } from '../../core/auth/auth.service';
import { PagedResult, RoleName, UserListItem } from '../../core/models/api.models';

class StubLoader {
  getTranslation(): Observable<Record<string, string>> {
    return of({});
  }
}

function user(overrides: Partial<UserListItem> = {}): UserListItem {
  return {
    id: 'user-1',
    username: 'jdoe',
    email: 'jane.doe@example.com',
    firstName: 'Jane',
    lastName: 'Doe',
    roleId: 2,
    role: RoleName.user,
    isDeleted: false,
    createdAt: '2026-08-01T07:00:00.000+00:00',
    lastModifiedAt: null,
    lastLoginAt: null,
    isLockedOut: false,
    deletedAt: null,
    deletedBy: null,
    ...overrides,
  };
}

function page(items: readonly UserListItem[]): PagedResult<UserListItem> {
  return {
    items,
    pageNumber: 1,
    pageSize: 10,
    totalCount: items.length,
    totalPages: items.length === 0 ? 0 : 1,
    hasPreviousPage: false,
    hasNextPage: false,
  };
}

function sessionFor(role: string) {
  return {
    accessToken: 'token',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 'me', username: 'someone', email: 'a@b.c', firstName: 'A', lastName: 'B', role },
  };
}

describe('UsersListPage', () => {
  let fixture: ComponentFixture<UsersListPage>;
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTransloco({
          config: { availableLangs: ['en', 'ar'], defaultLang: 'en' },
          loader: StubLoader,
        }),
      ],
    });

    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.applySession(sessionFor(RoleName.admin));

    fixture = TestBed.createComponent(UsersListPage);
  });

  function flushRoles(): void {
    http
      .match((request) => request.url.includes('/roles'))
      .forEach((request) => request.flush([{ id: 1, name: RoleName.admin }, { id: 2, name: RoleName.user }]));
  }

  function flushUsers(result: PagedResult<UserListItem>): void {
    const requests = http.match((request) => request.url.includes('/users'));

    expect(requests.length).toBeGreaterThan(0);
    requests.forEach((request) => request.flush(result));
  }

  it('renders a row per user', async () => {
    fixture.detectChanges();
    flushRoles();
    flushUsers(page([user(), user({ id: 'user-2', username: 'asmith', email: 'asmith@example.com' })]));

    await fixture.whenStable();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('jdoe');
    expect(fixture.nativeElement.textContent).toContain('asmith');
  });

  it('shows the empty state rather than a bare table when nothing matches', async () => {
    fixture.detectChanges();
    flushRoles();
    flushUsers(page([]));

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-empty-state')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it('shows the error state with a retry when the list fails to load', async () => {
    fixture.detectChanges();
    flushRoles();

    http
      .match((request) => request.url.includes('/users'))
      .forEach((request) =>
        request.flush({ errorCode: 'INTERNAL_ERROR' }, { status: 500, statusText: 'Server Error' }),
      );

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-error-state')).toBeTruthy();
  });

  it('offers create, edit and delete controls to an administrator', async () => {
    fixture.detectChanges();
    flushRoles();
    flushUsers(page([user()]));

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/users/new"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('a[href*="/edit"]')).toBeTruthy();
  });

  it.each([RoleName.user, RoleName.readOnly])('hides every mutation control from %s', async (role) => {
    auth.applySession(sessionFor(role));

    fixture.detectChanges();
    flushRoles();
    flushUsers(page([user()]));

    await fixture.whenStable();
    fixture.detectChanges();

    // UX only: the API refuses these operations for both roles regardless, which the backend authorization
    // matrix test proves. This asserts the interface does not offer an action that would fail.
    expect(fixture.nativeElement.querySelector('a[href="/users/new"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href*="/edit"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-checkbox')).toBeNull();
  });

  it('asks the API for the page, size and sort taken from the URL', async () => {
    fixture.componentRef.setInput('pageNumber', 3);
    fixture.componentRef.setInput('pageSize', 25);
    fixture.componentRef.setInput('sortBy', 'username');
    fixture.componentRef.setInput('sortDirection', 'Ascending');

    fixture.detectChanges();
    flushRoles();

    const request = http.match((candidate) => candidate.url.includes('/users'))[0];

    // The URL is the single source of truth for list state, so what the user sees and what was queried cannot
    // disagree.
    expect(request.request.params.get('pageNumber')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('sortBy')).toBe('username');
    expect(request.request.params.get('sortDirection')).toBe('Ascending');

    request.flush(page([]));
  });

  it('reads deleted users from the separate admin route', async () => {
    fixture.componentRef.setInput('deleted', true);

    fixture.detectChanges();
    flushRoles();

    const request = http.match((candidate) => candidate.url.includes('/users/deleted'))[0];

    // A different route, not a flag on the main listing: soft-delete visibility is an authorization decision.
    expect(request).toBeTruthy();
    request.flush(page([user({ isDeleted: true, deletedAt: '2026-08-10T00:00:00Z', deletedBy: 'admin' })]));
  });

  it('survives a failed role list without taking the page down', async () => {
    fixture.detectChanges();

    http
      .match((request) => request.url.includes('/roles'))
      .forEach((request) => request.flush(null, { status: 500, statusText: 'Server Error' }));

    flushUsers(page([user()]));

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
  });
});
