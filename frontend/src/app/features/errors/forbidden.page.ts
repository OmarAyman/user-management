import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Where the role guard sends a signed-in user without the required role.
 *
 * An explicit page rather than a silent redirect home: a redirect makes a permissions problem look like a
 * broken link, and leaves the user clicking the same nav item again.
 */
@Component({
  selector: 'app-forbidden-page',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="forbidden" *transloco="let t">
      <mat-icon aria-hidden="true">lock</mat-icon>
      <h1>{{ t('forbidden.title') }}</h1>
      <p>{{ t('forbidden.message') }}</p>

      <a matButton="filled" routerLink="/users">{{ t('forbidden.back') }}</a>
    </div>
  `,
  styles: `
    .forbidden {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.75rem;
      padding: 4rem 1rem;
      text-align: center;
    }

    mat-icon {
      font-size: 3rem;
      width: 3rem;
      height: 3rem;
      color: var(--mat-sys-on-surface-variant);
    }

    h1 {
      margin: 0;
      font-size: 1.5rem;
    }
  `,
})
export class ForbiddenPage {}
