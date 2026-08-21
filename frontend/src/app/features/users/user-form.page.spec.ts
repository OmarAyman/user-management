import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { UserFormPage } from './user-form.page';
import { RoleName, UserDetails } from '../../core/models/api.models';

class StubLoader {
  getTranslation(): Observable<Record<string, string>> {
    return of({});
  }
}

const EXISTING: UserDetails = {
  id: 'user-7',
  username: 'khalil',
  email: 'khalil@example.com',
  firstName: 'Khalil',
  lastName: 'Rahman',
  roleId: 3,
  role: RoleName.readOnly,
  isDeleted: false,
  createdAt: '2026-08-01T07:00:00.000+00:00',
  createdBy: 'admin',
  lastModifiedAt: null,
  lastModifiedBy: null,
  lastLoginAt: null,
  deletedAt: null,
  deletedBy: null,
  rowVersion: 'AAAAAAAAB9E=',
};

/**
 * The edit form, and the pitfall that made it ship empty.
 *
 * `id` is a signal input bound from the route, and Angular assigns route-bound inputs *after* constructing the
 * component. The first version read `this.id()` in the constructor, got `undefined`, and never fetched the
 * user - so /users/:id/edit rendered the right heading over four blank fields. The template was not the
 * problem: it reads the signal later, once it has a value, which is exactly why the bug looked like a data
 * problem rather than a timing one.
 *
 * Nothing caught it. The browser suite opened the edit route and scanned it for accessibility violations, and
 * an empty form is perfectly accessible. These tests assert the values.
 */
describe('UserFormPage', () => {
  let fixture: ComponentFixture<UserFormPage>;
  let http: HttpTestingController;

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
    fixture = TestBed.createComponent(UserFormPage);
  });

  function flushRoles(): void {
    http
      .match((request) => request.url.includes('/roles'))
      .forEach((request) =>
        request.flush([
          { id: 1, name: RoleName.admin },
          { id: 2, name: RoleName.user },
          { id: 3, name: RoleName.readOnly },
        ]),
      );
  }

  /** Sets the route-bound input the way the router does, then lets the effect run. */
  function editing(id: string): void {
    fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
  }

  it('fetches the user once the route input arrives', () => {
    editing(EXISTING.id);
    flushRoles();

    // The request the original version never made.
    const request = http.expectOne((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`));
    expect(request.request.method).toBe('GET');

    request.flush(EXISTING);
  });

  it('populates every editable field from the response', () => {
    editing(EXISTING.id);
    flushRoles();
    http.expectOne((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`)).flush(EXISTING);
    fixture.detectChanges();

    const value = fixture.componentInstance['form'].getRawValue();

    expect(value.username).toBe(EXISTING.username);
    expect(value.email).toBe(EXISTING.email);
    expect(value.firstName).toBe(EXISTING.firstName);
    expect(value.lastName).toBe(EXISTING.lastName);

    // The role the user actually has, not the form's create-mode default of 2.
    expect(value.roleId).toBe(EXISTING.roleId);
  });

  it('disables the fields the API will not let you change here', () => {
    editing(EXISTING.id);
    flushRoles();
    http.expectOne((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`)).flush(EXISTING);
    fixture.detectChanges();

    // Username is immutable server-side and a password change is its own endpoint. Both being *enabled* was a
    // visible symptom of the load never happening.
    expect(fixture.componentInstance['form'].controls.username.disabled).toBe(true);
    expect(fixture.componentInstance['form'].controls.password.disabled).toBe(true);
  });

  it('keeps the concurrency token so a save can prove which version was edited', () => {
    editing(EXISTING.id);
    flushRoles();
    http.expectOne((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`)).flush(EXISTING);
    fixture.detectChanges();

    expect(fixture.componentInstance['rowVersion']).toBe(EXISTING.rowVersion);
  });

  it('fetches nothing when there is no id, which is the create form', () => {
    fixture.detectChanges();
    flushRoles();

    // A create form that quietly GETs /users/undefined is the mirror-image mistake.
    http.expectNone((candidate) => /\/users\/[^/]+$/.test(candidate.url));
  });

  it('does not re-fetch when unrelated state changes', () => {
    editing(EXISTING.id);
    flushRoles();
    http.expectOne((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`)).flush(EXISTING);

    fixture.detectChanges();
    fixture.detectChanges();

    // The effect reads a signal, so without a guard it would re-request on every change detection.
    http.expectNone((candidate) => candidate.url.endsWith(`/users/${EXISTING.id}`));
  });
});
