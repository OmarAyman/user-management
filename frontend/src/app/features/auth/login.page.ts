import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/http/api-error.model';
import { NotificationService } from '../../core/services/notification.service';
import { LocaleService } from '../../core/services/locale.service';

@Component({
  selector: 'app-login-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    ReactiveFormsModule,
    TranslocoDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="login-shell" *transloco="let t">
      <mat-card class="login-card">
        <mat-card-header>
          <mat-card-title>{{ t('auth.login.title') }}</mat-card-title>
          <mat-card-subtitle>{{ t('auth.login.subtitle') }}</mat-card-subtitle>
        </mat-card-header>

        @if (submitting()) {
          <mat-progress-bar mode="indeterminate" [attr.aria-label]="t('auth.login.signingIn')" />
        }

        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <!--
              The failure message is a live region: without it a screen-reader user submits, hears nothing, and
              has no way to know the attempt was rejected.
            -->
            @if (failure(); as message) {
              <p class="login-error" role="alert">{{ message }}</p>
            }

            <mat-form-field appearance="outline">
              <mat-label>{{ t('common.fields.username') }}</mat-label>
              <input
                matInput
                formControlName="username"
                autocomplete="username"
                dir="auto"
                required
              />
              @if (form.controls.username.hasError('required') && form.controls.username.touched) {
                <mat-error>{{ t('users.validation.required') }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>{{ t('common.fields.password') }}</mat-label>
              <input
                matInput
                formControlName="password"
                [type]="passwordVisible() ? 'text' : 'password'"
                autocomplete="current-password"
                required
              />
              <button
                matIconButton
                matSuffix
                type="button"
                [attr.aria-label]="passwordVisible() ? t('auth.login.hidePassword') : t('auth.login.showPassword')"
                (click)="passwordVisible.set(!passwordVisible())"
              >
                <mat-icon>{{ passwordVisible() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
              @if (form.controls.password.hasError('required') && form.controls.password.touched) {
                <mat-error>{{ t('users.validation.required') }}</mat-error>
              }
            </mat-form-field>

            <button
              matButton="filled"
              type="submit"
              class="login-submit"
              [disabled]="submitting()"
            >
              {{ submitting() ? t('auth.login.signingIn') : t('auth.login.submit') }}
            </button>
          </form>
        </mat-card-content>

        <mat-card-actions align="end">
          <button matButton type="button" (click)="locale.toggle()">
            {{ locale.locale() === 'en' ? t('common.language.switchToArabic') : t('common.language.switchToEnglish') }}
          </button>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: `
    .login-shell {
      display: grid;
      place-items: center;
      min-height: 100dvh;
      padding: 1rem;
    }

    .login-card {
      width: min(420px, 100%);
    }

    form {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding-block-start: 1rem;
    }

    .login-submit {
      margin-block-start: 0.5rem;
    }

    .login-error {
      margin: 0 0 0.5rem;
      padding: 0.75rem;
      border-radius: 4px;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly locale = inject(LocaleService);
  protected readonly submitting = signal(false);
  protected readonly passwordVisible = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(50)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    const { username, password } = this.form.getRawValue();

    this.auth.login(username, password).subscribe({
      next: () => {
        this.submitting.set(false);

        // Honours the returnUrl the guard captured, so a deep link survives sign-in.
        const returnUrl = new URLSearchParams(window.location.search).get('returnUrl');
        void this.router.navigateByUrl(returnUrl ?? '/users');
      },
      error: (error: ApiError) => {
        this.submitting.set(false);

        // Rendered inline rather than as a toast: a sign-in failure belongs next to the form that caused it,
        // and a toast can be missed or dismissed before it is read.
        this.failure.set(this.notifications.describe(error));
      },
    });
  }
}

