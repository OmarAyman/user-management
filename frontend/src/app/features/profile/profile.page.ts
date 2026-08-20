import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';

import { ApiError, CONFLICT_FIELD, ErrorCode } from '../../core/http/api-error.model';
import { UserDetails } from '../../core/models/api.models';
import { NotificationService } from '../../core/services/notification.service';
import { UsersApiService } from '../../core/services/users-api.service';
import { LoadingStateComponent } from '../../shared/components/state-panels';

const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{12,128}$/;

/**
 * The caller's own profile.
 *
 * There is no role control anywhere on this page, and the API's profile model has no role field either - so
 * self-elevation is not something the UI declines to offer, it is something the contract cannot express.
 */
@Component({
  selector: 'app-profile-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    ReactiveFormsModule,
    TranslocoDirective,
    LoadingStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './profile.page.html',
  styleUrl: './profile.page.scss',
})
export class ProfilePage {
  private readonly usersApi = inject(UsersApiService);
  private readonly notifications = inject(NotificationService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly profile = signal<UserDetails | null>(null);
  protected readonly loading = signal(true);
  protected readonly savingProfile = signal(false);
  protected readonly savingPassword = signal(false);
  protected readonly conflict = signal<string | null>(null);

  protected readonly profileForm = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  });

  protected readonly passwordForm = this.formBuilder.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);

    this.usersApi.getMyProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.profileForm.patchValue({
          firstName: profile.firstName,
          lastName: profile.lastName,
          email: profile.email,
        });
        this.loading.set(false);
      },
      error: (error: ApiError) => {
        this.loading.set(false);
        this.notifications.error(error);
      },
    });
  }

  protected saveProfile(): void {
    const current = this.profile();

    if (this.profileForm.invalid || current === null || this.savingProfile()) {
      this.profileForm.markAllAsTouched();

      return;
    }

    this.savingProfile.set(true);
    this.conflict.set(null);

    this.usersApi
      .updateMyProfile({ ...this.profileForm.getRawValue(), rowVersion: current.rowVersion })
      .subscribe({
        next: (updated) => {
          this.savingProfile.set(false);

          // The response carries a fresh token, so a second save in the same visit does not fail as stale.
          this.profile.set(updated);
          this.notifications.success('profile.updatedSuccess');
        },
        error: (error: ApiError) => {
          this.savingProfile.set(false);

          const field = CONFLICT_FIELD[error.code];

          if (error.kind === 'conflict' && field !== undefined) {
            this.profileForm.get(field)?.setErrors({ taken: true });

            return;
          }

          if (error.code === ErrorCode.resourceModified) {
            this.conflict.set(this.notifications.describe(error));

            return;
          }

          this.notifications.error(error);
        },
      });
  }

  protected changePassword(): void {
    if (this.passwordForm.invalid || this.savingPassword()) {
      this.passwordForm.markAllAsTouched();

      return;
    }

    const { currentPassword, newPassword } = this.passwordForm.getRawValue();

    if (currentPassword === newPassword) {
      this.passwordForm.controls.newPassword.setErrors({ mustDiffer: true });

      return;
    }

    this.savingPassword.set(true);

    this.usersApi.changeMyPassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.passwordForm.reset();

        // Says plainly that other sessions ended: a user who changed their password because they were worried
        // needs to know it took effect everywhere.
        this.notifications.success('profile.passwordChanged');
      },
      error: (error: ApiError) => {
        this.savingPassword.set(false);

        if (error.code === ErrorCode.invalidCredentials) {
          this.passwordForm.controls.currentPassword.setErrors({ wrong: true });

          return;
        }

        if (error.kind === 'validation') {
          const messages = error.fieldErrors['newPassword'];

          if (messages !== undefined) {
            this.passwordForm.controls.newPassword.setErrors({ server: messages.join(' ') });

            return;
          }
        }

        this.notifications.error(error);
      },
    });
  }
}
