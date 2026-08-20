import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { AuditApiService } from '../../core/services/audit-api.service';
import { ApiError } from '../../core/http/api-error.model';
import { AuditLogEntry, PagedResult } from '../../core/models/api.models';
import { NotificationService } from '../../core/services/notification.service';
import { EmptyStateComponent, ErrorStateComponent, LoadingStateComponent } from '../../shared/components/state-panels';

/** The actions the API can record. Kept here so the filter offers exactly what exists. */
const AUDIT_ACTIONS = ['Insert', 'Update', 'Delete', 'Restore', 'RoleChange'] as const;

/**
 * The audit trail.
 *
 * Renders the before/after values as a field-by-field diff. Redacted values arrive as "***" from the server
 * and are labelled as redacted rather than shown raw, so a reader can see that a credential changed without
 * being shown anything about it.
 */
@Component({
  selector: 'app-audit-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    MatTableModule,
    TranslocoDirective,
    LoadingStateComponent,
    EmptyStateComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit.page.html',
  styleUrl: './audit.page.scss',
})
export class AuditPage {
  private readonly auditApi = inject(AuditApiService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly transloco = inject(TranslocoService);

  readonly pageNumber = input(1, { transform: (value: string | number) => Number(value) || 1 });
  readonly pageSize = input(25, { transform: (value: string | number) => Number(value) || 25 });
  readonly action = input<string | undefined>(undefined);

  protected readonly page = signal<PagedResult<AuditLogEntry> | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actions = AUDIT_ACTIONS;
  protected readonly columns = ['timestamp', 'action', 'target', 'actor', 'ipAddress', 'changes'];

  constructor() {
    effect(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.auditApi
      .list({ pageNumber: this.pageNumber(), pageSize: this.pageSize(), action: this.action() })
      .subscribe({
        next: (result) => {
          this.page.set(result);
          this.loading.set(false);
        },
        error: (error: ApiError) => {
          this.loading.set(false);
          this.page.set(null);
          this.loadError.set(this.notifications.describe(error));
        },
      });
  }

  protected onPageChange(event: PageEvent): void {
    void this.router.navigate([], {
      queryParams: { pageNumber: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  protected onActionChange(action: string | null): void {
    void this.router.navigate([], {
      queryParams: { action: action ?? undefined, pageNumber: 1 },
      queryParamsHandling: 'merge',
    });
  }

  protected actionLabel(action: string): string {
    const key = `audit.actions.${action}`;
    const translated = this.transloco.translate(key);

    return translated === key ? action : translated;
  }

  /**
   * Flattens old and new values into one list of changed fields.
   *
   * Only keys present in either side appear, because the server already records just the properties that
   * changed - showing the whole entity would bury the one field that moved.
   */
  protected changes(entry: AuditLogEntry): readonly { field: string; from: unknown; to: unknown }[] {
    const keys = new Set([...Object.keys(entry.oldValues ?? {}), ...Object.keys(entry.newValues ?? {})]);

    return [...keys].map((field) => ({
      field,
      from: entry.oldValues?.[field],
      to: entry.newValues?.[field],
    }));
  }

  protected format(value: unknown): string {
    if (value === undefined) {
      return '—';
    }

    if (value === null) {
      return '∅';
    }

    // The server writes "***" for redacted values; label it so a reader is not left guessing what it means.
    if (value === '***') {
      return this.transloco.translate('audit.redacted');
    }

    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }
}
