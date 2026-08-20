import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import {
  Availability,
  PagedResult,
  Role,
  UserDetails,
  UserListItem,
  UserListQuery,
} from '../models/api.models';

/** Everything the users and roles endpoints offer, typed. No component builds a URL itself. */
@Injectable({ providedIn: 'root' })
export class UsersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(query: UserListQuery): Observable<PagedResult<UserListItem>> {
    return this.http.get<PagedResult<UserListItem>>(`${this.baseUrl}/users`, { params: toParams(query) });
  }

  /**
   * The Admin-only deleted listing. A separate method because it is a separate route: soft-delete visibility
   * is an authorization decision on the server, not a flag this client can set.
   */
  listDeleted(query: UserListQuery): Observable<PagedResult<UserListItem>> {
    return this.http.get<PagedResult<UserListItem>>(`${this.baseUrl}/users/deleted`, {
      params: toParams(query),
    });
  }

  getById(id: string): Observable<UserDetails> {
    return this.http.get<UserDetails>(`${this.baseUrl}/users/${id}`);
  }

  create(request: {
    username: string;
    email: string;
    firstName: string;
    lastName: string;
    password: string;
    roleId: number;
  }): Observable<UserDetails> {
    return this.http.post<UserDetails>(`${this.baseUrl}/users`, request);
  }

  update(
    id: string,
    request: { email: string; firstName: string; lastName: string; roleId: number; rowVersion: string },
  ): Observable<UserDetails> {
    return this.http.put<UserDetails>(`${this.baseUrl}/users/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/users/${id}`);
  }

  restore(id: string, rowVersion: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/users/${id}/restore`, { rowVersion });
  }

  getMyProfile(): Observable<UserDetails> {
    return this.http.get<UserDetails>(`${this.baseUrl}/users/me`);
  }

  updateMyProfile(request: {
    firstName: string;
    lastName: string;
    email: string;
    rowVersion: string;
  }): Observable<UserDetails> {
    return this.http.put<UserDetails>(`${this.baseUrl}/users/me`, request);
  }

  changeMyPassword(request: { currentPassword: string; newPassword: string }): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/users/me/change-password`, request);
  }

  /** Backs the async form validators. Advisory only - the unique index decides. */
  checkAvailability(query: {
    username?: string;
    email?: string;
    excludeUserId?: string;
  }): Observable<Availability> {
    return this.http.get<Availability>(`${this.baseUrl}/users/availability`, { params: toParams(query) });
  }

  getRoles(): Observable<readonly Role[]> {
    return this.http.get<readonly Role[]>(`${this.baseUrl}/roles`);
  }
}

/** Drops empty values so the URL carries only what was actually asked for. */
function toParams(source: object): HttpParams {
  let params = new HttpParams();

  for (const [key, value] of Object.entries(source) as readonly [string, unknown][]) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }

  return params;
}

