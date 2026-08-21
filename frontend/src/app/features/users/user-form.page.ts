import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import {
  AbstractControl,
  AsyncValidatorFn,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Observable, map, of } from 'rxjs';

import { ApiError } from '../../core/http/api-error.model';
import { CONFLICT_FIELD, ErrorCode } from '../../core/http/api-error.model';
import { Role, UserDetails } from '../../core/models/api.models';
import { NotificationService } from '../../core/services/notification.service';
import { UsersApiService } from '../../core/services/users-api.service';

/** Matches the server's policy, which is the authority. Duplicated here only so the user is told early. */
const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{12,128}$/;

@Component({
  selector: 'app-user-form-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './user-form.page.html',
  styleUrl: './user-form.page.scss',
})
export class UserFormPage {
  private readonly usersApi = inject(UsersApiService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly formBuilder = inject(FormBuilder);

  /** Bound from the route. Absent means "create". */
  readonly id = input<string | undefined>(undefined);

  protected readonly roles = signal<readonly Role[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly conflict = signal<string | null>(null);
  protected readonly isEdit = computed(() => this.id() !== undefined);

  /** Held so it can be sent back on save: it is the proof of which version was edited. */
  private rowVersion = '';

  /** The id already fetched, so the effect does not re-request on every unrelated signal change. */
  private loadedId: string | undefined;

  protected readonly form = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50), Validators.pattern(/^[a-zA-Z0-9._-]+$/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    roleId: [2, [Validators.required]],
  });

  constructor() {
    this.usersApi.getRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error: ApiError) => this.loadError.set(this.notifications.describe(error)),
    });

    // Async uniqueness checks run on blur, not per keystroke: a request per character is a scraper, and the
    // unique index is the authority anyway (ADR-0016).
    this.form.controls.username.addAsyncValidators(this.availabilityValidator('username'));
    this.form.controls.email.addAsyncValidators(this.availabilityValidator('email'));
    this.form.controls.username.updateValueAndValidity({ emitEvent: false });

    // Loading has to wait for the input to exist.
    //
    // `id` is a signal input bound from the route, and Angular assigns route-bound inputs *after* the component
    // is constructed - so reading it here returns undefined however the URL looks. The original version did
    // exactly that, so on /users/:id/edit the fetch never ran: the heading said "Edit user" (the template reads
    // the signal later, once it is set) while every field stayed empty and the username stayed editable.
    //
    // An effect reads the input once it is there, and re-runs if the id changes while the component is reused -
    // which ngOnInit would not.
    effect(() => {
      const id = this.id();

      if (id === undefined || id === this.loadedId) {
        return;
      }

      this.loadedId = id;
      this.loadUser(id);
    });
  }

  protected save(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.conflict.set(null);

    const value = this.form.getRawValue();
    const id = this.id();

    const request$ =
      id === undefined
        ? this.usersApi.create({
            username: value.username,
            email: value.email,
            firstName: value.firstName,
            lastName: value.lastName,
            password: value.password,
            roleId: value.roleId,
          })
        : this.usersApi.update(id, {
            email: value.email,
            firstName: value.firstName,
            lastName: value.lastName,
            roleId: value.roleId,
            rowVersion: this.rowVersion,
          });

    request$.subscribe({
      next: () => {
        this.submitting.set(false);
        this.notifications.success(id === undefined ? 'users.form.createdSuccess' : 'users.form.updatedSuccess');
        void this.router.navigate(['/users']);
      },
      error: (error: ApiError) => {
        this.submitting.set(false);
        this.handleSaveError(error);
      },
    });
  }

  private handleSaveError(error: ApiError): void {
    // A conflict is put on the field that caused it, so the user sees it where they can fix it rather than in
    // a toast that names no field.
    const field = CONFLICT_FIELD[error.code];

    if (error.kind === 'conflict' && field !== undefined) {
      this.form.get(field)?.setErrors({ taken: true });
      this.form.get(field)?.markAsTouched();

      return;
    }

    // Someone else changed the row: the edit is kept on screen and the user is told to reload, because
    // silently discarding their work - or blindly retrying - would be worse (ADR-0013).
    if (error.code === ErrorCode.resourceModified) {
      this.conflict.set(this.notifications.describe(error));

      return;
    }

    if (error.kind === 'validation') {
      for (const [name, messages] of Object.entries(error.fieldErrors)) {
        this.form.get(name)?.setErrors({ server: messages.join(' ') });
      }

      return;
    }

    this.notifications.error(error);
  }

  private loadUser(id: string): void {
    this.loading.set(true);

    // Username is immutable server-side, and password has its own endpoint - so neither is editable here.
    this.form.controls.username.disable();
    this.form.controls.password.disable();

    this.usersApi.getById(id).subscribe({
      next: (user: UserDetails) => {
        this.rowVersion = user.rowVersion;
        this.form.patchValue({
          username: user.username,
          email: user.email,
          firstName: user.firstName,
          lastName: user.lastName,
          roleId: user.roleId,
        });
        this.loading.set(false);
      },
      error: (error: ApiError) => {
        this.loading.set(false);
        this.loadError.set(this.notifications.describe(error));
      },
    });
  }

  /**
   * Checks availability against active users.
   *
   * `updateOn: 'blur'` on the control would apply to the synchronous validators too, so debouncing is handled
   * by only running this when the control is dirty and settled - Angular runs async validators after the
   * synchronous ones pass, which already keeps it off every keystroke of an invalid value.
   */
  private availabilityValidator(field: 'username' | 'email'): AsyncValidatorFn {
    return (control: AbstractControl): Observable<ValidationErrors | null> => {
      const value = String(control.value ?? '').trim();

      if (value === '' || control.pristine) {
        return of(null);
      }

      return this.usersApi
        .checkAvailability({ [field]: value, excludeUserId: this.id() })
        .pipe(
          map((availability) => {
            const available = field === 'username' ? availability.usernameAvailable : availability.emailAvailable;

            return available === false ? { taken: true } : null;
          }),
          // A failed availability check must not block a submit: the server's unique index still decides, and
          // reporting "taken" because a probe failed would be a false negative.
          map((result) => result),
        );
    };
  }
}
