import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import { AuditLogEntry, PagedResult, SortDirection } from '../models/api.models';

export interface AuditLogQuery {
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly entityId?: string;
  readonly action?: string;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly sortBy?: string;
  readonly sortDirection?: SortDirection;
}

/** Read-only, matching the API: there is no route that writes an audit entry. */
@Injectable({ providedIn: 'root' })
export class AuditApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(query: AuditLogQuery): Observable<PagedResult<AuditLogEntry>> {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }

    return this.http.get<PagedResult<AuditLogEntry>>(`${this.baseUrl}/audit-logs`, { params });
  }
}
