import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/http/api-error.model';
import { PagedResult, Role, RoleName, SortDirection, UserListItem } from '../../core/models/api.models';
import { NotificationService } from '../../core/services/notification.service';
import { UsersApiService } from '../../core/services/users-api.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog';
import { EmptyStateComponent, ErrorStateComponent, LoadingStateComponent } from '../../shared/components/state-panels';
import { HasRoleDirective } from '../../shared/directives/has-role.directive';

/**
 * The user list: search, role filter, column sort, paging, and the Admin-only deleted view.
 *
 * Every one of those lives in the URL rather than in component state. That is what makes a filtered list
 * shareable, the browser's back button meaningful, and a reload land where the user was - and it removes the
 * class of bug where the visible controls and the executed query disagree.
 */
@Component({
  selector: 'app-users-list-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    MatSortModule,
    MatTableModule,
    MatTooltipModule,
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
    HasRoleDirective,
    LoadingStateComponent,
    EmptyStateComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './users-list.page.html',
  styleUrl: './users-list.page.scss',
})
export class UsersListPage {
  private readonly usersApi = inject(UsersApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly transloco = inject(TranslocoService);

  protected readonly auth = inject(AuthService);
  protected readonly adminRole = RoleName.admin;

  /** Query params, bound straight to inputs by the router. */
  readonly pageNumber = input(1, { transform: toPositiveInt(1) });
  readonly pageSize = input(10, { transform: toPositiveInt(10) });
  readonly search = input('');
  readonly roleId = input(undefined, { transform: toOptionalInt });
  readonly sortBy = input('createdAt');
  readonly sortDirection = input<SortDirection>('Descending');
  readonly deleted = input(false, { transform: toBoolean });

  protected readonly page = signal<PagedResult<UserListItem> | null>(null);
  protected readonly roles = signal<readonly Role[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly searchControl = new FormControl('', { nonNullable: true });

  protected readonly columns = computed(() =>
    this.deleted()
      ? ['username', 'email', 'role', 'deletedAt', 'deletedBy', 'actions']
      : ['username', 'email', 'role', 'status', 'createdAt', 'actions'],
  );

  constructor() {
    this.usersApi.getRoles().subscribe({
      next: (roles) => this.roles.set(roles),

      // A failed role list degrades the filter to "all roles" rather than taking the page down with it.
      error: () => this.roles.set([]),
    });

    // Debounced and de-duplicated: typing "smith" should issue one request, not five, and re-emitting an
    // unchanged term after a focus change should issue none.
    this.searchControl.valueChanges
      .pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => this.navigate({ search: term || undefined, pageNumber: 1 }));

    // Keeps the input in step when the URL changes from elsewhere - a back navigation, or a shared link.
    effect(() => {
      const current = this.search();

      if (this.searchControl.value !== current) {
        this.searchControl.setValue(current, { emitEvent: false });
      }
    });

    effect(() => this.load());
  }

  protected load(): void {
    const query = {
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      search: this.search() || undefined,
      roleId: this.roleId(),
      sortBy: this.sortBy(),
      sortDirection: this.sortDirection(),
    };

    this.loading.set(true);
    this.loadError.set(null);

    const request = this.deleted() ? this.usersApi.listDeleted(query) : this.usersApi.list(query);

    request.subscribe({
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

  protected onSortChange(sort: Sort): void {
    this.navigate({
      // An empty direction means the user cycled the column off; the default ordering is the honest fallback.
      sortBy: sort.direction === '' ? 'createdAt' : sort.active,
      sortDirection: sort.direction === 'asc' ? 'Ascending' : 'Descending',
      pageNumber: 1,
    });
  }

  protected onPageChange(event: PageEvent): void {
    this.navigate({ pageNumber: event.pageIndex + 1, pageSize: event.pageSize });
  }

  protected onRoleFilterChange(roleId: number | null): void {
    this.navigate({ roleId: roleId ?? undefined, pageNumber: 1 });
  }

  protected onDeletedToggle(showDeleted: boolean): void {
    this.navigate({ deleted: showDeleted || undefined, pageNumber: 1 });
  }

  protected clearFilters(): void {
    void this.router.navigate(['/users']);
  }

  protected confirmDelete(user: UserListItem): void {
    this.confirm({
      titleKey: 'users.list.confirmDelete.title',
      messageKey: 'users.list.confirmDelete.message',
      messageParams: { username: user.username },
      confirmKey: 'users.list.confirmDelete.confirm',
      destructive: true,
    }).then((confirmed) => {
      if (!confirmed) {
        return;
      }

      this.usersApi.delete(user.id).subscribe({
        next: () => {
          this.notifications.success('users.list.deleted_success');
          this.load();
        },
        error: (error: ApiError) => this.notifications.error(error),
      });
    });
  }

  protected confirmRestore(user: UserListItem): void {
    this.confirm({
      titleKey: 'users.list.confirmRestore.title',
      messageKey: 'users.list.confirmRestore.message',
      messageParams: { username: user.username },
      confirmKey: 'users.list.confirmRestore.confirm',
    }).then((confirmed) => {
      if (!confirmed) {
        return;
      }

      // Restore needs the current concurrency token, and the list does not carry one - so the row is re-read
      // first. That extra call is the price of not letting a client guess a version.
      this.usersApi.getById(user.id).subscribe({
        next: (details) =>
          this.usersApi.restore(details.id, details.rowVersion).subscribe({
            next: () => {
              this.notifications.success('users.list.restored_success');
              this.load();
            },
            error: (error: ApiError) => this.notifications.error(error),
          }),
        error: (error: ApiError) => this.notifications.error(error),
      });
    });
  }

  protected roleLabel(role: string): string {
    return this.transloco.translate(`roles.${role}`);
  }

  private confirm(data: ConfirmDialogData): Promise<boolean> {
    return new Promise((resolve) => {
      this.dialog
        .open(ConfirmDialogComponent, { data, width: '420px', autoFocus: 'dialog' })
        .afterClosed()
        .subscribe((result) => resolve(result === true));
    });
  }

  private navigate(params: Record<string, string | number | boolean | undefined>): void {
    void this.router.navigate([], {
      queryParams: params,
      queryParamsHandling: 'merge',
    });
  }
}

/*
  Query params arrive as strings, or as undefined when absent, so every transform has to accept `unknown` -
  that is the signature Angular's input transform contract uses. Coercing here rather than in the template
  keeps a malformed URL (?pageNumber=abc) from reaching the API as garbage.
*/
function toPositiveInt(fallback: number) {
  return (value: unknown): number => {
    const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);

    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
  };
}

function toOptionalInt(value: unknown): number | undefined {
  if (value === undefined || value === null || value === '') {
    return undefined;
  }

  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value), 10);

  return Number.isFinite(parsed) ? parsed : undefined;
}

function toBoolean(value: unknown): boolean {
  return value === true || value === 'true';
}


