import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Loading, empty and error panels.
 *
 * Three small components rather than one with a mode flag: each has different content, different affordances
 * (only the error state offers a retry) and different accessibility semantics. A single component with a mode
 * would need conditionals for all three anyway.
 */
@Component({
  selector: 'app-loading-state',
  imports: [MatProgressSpinnerModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-state" *transloco="let t">
      <mat-spinner diameter="40" />
      <!-- Announced politely: a screen-reader user otherwise has no signal that a request is in flight. -->
      <p role="status" aria-live="polite">{{ t('common.states.loading') }}</p>
    </div>
  `,
  styles: `
    .app-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 1rem;
      padding: 3rem 1rem;
      text-align: center;
    }
  `,
})
export class LoadingStateComponent {}

@Component({
  selector: 'app-empty-state',
  imports: [MatIconModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-state" *transloco="let t">
      <mat-icon aria-hidden="true">search_off</mat-icon>
      <h3>{{ t(titleKey()) }}</h3>
      <p>{{ t(messageKey()) }}</p>
    </div>
  `,
  styles: `
    .app-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      padding: 3rem 1rem;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-icon {
      font-size: 2.5rem;
      width: 2.5rem;
      height: 2.5rem;
    }
  `,
})
export class EmptyStateComponent {
  readonly titleKey = input('common.states.emptyTitle');
  readonly messageKey = input('common.states.emptyMessage');
}

@Component({
  selector: 'app-error-state',
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-state" *transloco="let t">
      <mat-icon color="warn" aria-hidden="true">error_outline</mat-icon>

      <!-- assertive: a failed load is exactly the case a user needs told about immediately. -->
      <h3 role="alert">{{ t('common.states.errorTitle') }}</h3>
      <p>{{ message() }}</p>

      <button matButton="filled" type="button" (click)="retry.emit()">
        {{ t('common.actions.retry') }}
      </button>
    </div>
  `,
  styles: `
    .app-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.75rem;
      padding: 3rem 1rem;
      text-align: center;
    }

    mat-icon {
      font-size: 2.5rem;
      width: 2.5rem;
      height: 2.5rem;
    }
  `,
})
export class ErrorStateComponent {
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
