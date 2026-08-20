import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';

export interface ConfirmDialogData {
  readonly titleKey: string;
  readonly messageKey: string;
  readonly messageParams?: Record<string, unknown>;
  readonly confirmKey: string;
  readonly destructive?: boolean;
}

/**
 * A confirmation step for actions that are hard to reverse.
 *
 * Material's dialog traps focus and restores it to the trigger on close, which is what makes this usable by
 * keyboard - the reason for a dialog rather than a `confirm()` call. The confirm button carries the specific
 * verb ("Delete") rather than "OK", so the consequence is legible without reading the body text.
 *
 * Focus lands on the dialog container rather than on the confirm button (`autoFocus: 'dialog'` at the call
 * site). That is deliberate for a destructive confirmation: a screen reader reads the whole question, and a
 * stray Enter does not activate Delete.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatButtonModule, MatDialogModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h2 mat-dialog-title>{{ t(data.titleKey) }}</h2>

      <mat-dialog-content>
        <p>{{ t(data.messageKey, data.messageParams) }}</p>
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button matButton type="button" (click)="dialogRef.close(false)">
          {{ t('common.actions.cancel') }}
        </button>

        <!-- No cdkFocusInitial: the container takes focus instead, so Enter cannot immediately confirm a
             destructive action the user has not read yet. -->
        <button
          matButton="filled"
          type="button"
          [color]="data.destructive ? 'warn' : 'primary'"
          (click)="dialogRef.close(true)"
        >
          {{ t(data.confirmKey) }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
})
export class ConfirmDialogComponent {
  readonly dialogRef = inject<MatDialogRef<ConfirmDialogComponent, boolean>>(MatDialogRef);
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}
