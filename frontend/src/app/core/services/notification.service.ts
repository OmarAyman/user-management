import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';

import { ApiError } from '../http/api-error.model';

/**
 * User-facing messages.
 *
 * Server errors are rendered from the SPA's own catalogue, keyed by the server's stable error code, so the
 * wording matches the rest of the interface. `errors.unknown` is the mandatory fallback: a code the frontend
 * has not been taught yet degrades to a generic sentence rather than an empty toast.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  success(key: string, params?: Record<string, unknown>): void {
    this.show(this.transloco.translate(key, params), 'app-snack-success');
  }

  error(error: ApiError): void {
    this.show(this.describe(error), 'app-snack-error');
  }

  message(key: string, params?: Record<string, unknown>): void {
    this.show(this.transloco.translate(key, params), 'app-snack-info');
  }

  describe(error: ApiError): string {
    const key = `errors.${error.code}`;
    const translated = this.transloco.translate(key, {
      retryAfterMinutes: Math.ceil((error.kind === 'unauthenticated' || error.kind === 'rateLimited'
        ? (error.retryAfterSeconds ?? 0)
        : 0) / 60),
    });

    // Transloco returns the key itself when there is no entry, which is the signal to fall back.
    return translated === key ? this.transloco.translate('errors.unknown') : translated;
  }

  private show(message: string, panelClass: string): void {
    this.snackBar.open(message, this.transloco.translate('common.actions.dismiss'), {
      duration: 6000,
      panelClass,

      // Bottom-centre in both directions: a corner position has to be mirrored for RTL, and there is nothing
      // to gain from that here.
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    });
  }
}
