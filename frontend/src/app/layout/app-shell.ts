import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { AuthService } from '../core/auth/auth.service';
import { LocaleService } from '../core/services/locale.service';
import { RoleName } from '../core/models/api.models';
import { HasRoleDirective } from '../shared/directives/has-role.directive';

/**
 * The application frame: navigation, language switch and the user menu.
 *
 * Navigation items are role-gated for tidiness, not for security - the audit link is hidden from a non-Admin
 * because offering an action that would fail is poor UX, while the route guard and the API's policy are what
 * actually prevent access.
 */
@Component({
  selector: 'app-shell',
  imports: [
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatMenuModule,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslocoDirective,
    HasRoleDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <mat-toolbar class="shell-toolbar">
        <span class="shell-brand">{{ t('app.title') }}</span>

        <nav class="shell-nav" [attr.aria-label]="t('nav.menu')">
          <a matButton routerLink="/users" routerLinkActive="active-link">{{ t('nav.users') }}</a>
          <a matButton routerLink="/profile" routerLinkActive="active-link">{{ t('nav.profile') }}</a>

          <a
            *appHasRole="adminRole"
            matButton
            routerLink="/audit"
            routerLinkActive="active-link"
            >{{ t('nav.audit') }}</a
          >
        </nav>

        <!--
          The same destinations behind one button below 768px. Without this the toolbar is wider than a phone
          viewport and the whole page scrolls sideways - which a responsive test caught.
        -->
        <button
          matIconButton
          class="shell-nav-toggle"
          type="button"
          [matMenuTriggerFor]="navMenu"
          [attr.aria-label]="t('nav.menu')"
        >
          <mat-icon>menu</mat-icon>
        </button>

        <mat-menu #navMenu>
          <a mat-menu-item routerLink="/users">{{ t('nav.users') }}</a>
          <a mat-menu-item routerLink="/profile">{{ t('nav.profile') }}</a>
          <a *appHasRole="adminRole" mat-menu-item routerLink="/audit">{{ t('nav.audit') }}</a>
        </mat-menu>

        <span class="shell-spacer"></span>

        <button
          matIconButton
          type="button"
          [attr.aria-label]="t('common.language.label')"
          (click)="locale.toggle()"
        >
          <mat-icon>translate</mat-icon>
        </button>

        <button matButton [matMenuTriggerFor]="userMenu" type="button">
          <mat-icon>account_circle</mat-icon>
          <span class="shell-username">{{ auth.currentUser()?.username }}</span>
        </button>

        <mat-menu #userMenu>
          <div class="shell-menu-header">
            <strong>{{ auth.currentUser()?.firstName }} {{ auth.currentUser()?.lastName }}</strong>
            <small>{{ t('roles.' + (auth.currentUser()?.role ?? '')) }}</small>
          </div>

          <mat-divider />

          <button mat-menu-item type="button" (click)="signOut()">
            <mat-icon>logout</mat-icon>
            <span>{{ t('common.actions.signOut') }}</span>
          </button>
        </mat-menu>
      </mat-toolbar>

      <main class="shell-content">
        <router-outlet />
      </main>
    </ng-container>
  `,
  styles: `
    .shell-toolbar {
      display: flex;
      gap: 0.5rem;
      position: sticky;
      top: 0;
      z-index: 10;
    }

    .shell-brand {
      font-weight: 600;

      /* Logical property, so the gap sits on the correct side in Arabic without a mirrored stylesheet. */
      margin-inline-end: 1rem;
      white-space: nowrap;
    }

    .shell-nav {
      display: flex;
      gap: 0.25rem;
    }

    .shell-spacer {
      flex: 1 1 auto;
    }

    .active-link {
      font-weight: 700;
      text-decoration: underline;
    }

    .shell-menu-header {
      display: flex;
      flex-direction: column;
      padding: 0.75rem 1rem;
    }

    .shell-content {
      padding: 1.5rem;
      max-width: 1200px;
      margin-inline: auto;
    }

    .shell-nav-toggle {
      display: none;
    }

    /*
      The toolbar is the first thing to run out of room. Below 768px the inline links collapse into a menu and
      the username label is dropped, because the alternative is a page that scrolls sideways on a phone.
    */
    @media (max-width: 767px) {
      .shell-content {
        padding: 1rem 0.75rem;
      }

      .shell-nav {
        display: none;
      }

      .shell-nav-toggle {
        display: inline-flex;
      }

      .shell-username {
        display: none;
      }

      .shell-brand {
        font-size: 1rem;
        margin-inline-end: 0;
      }
    }
  `,
})
export class AppShell {
  protected readonly auth = inject(AuthService);
  protected readonly locale = inject(LocaleService);
  protected readonly adminRole = RoleName.admin;

  private readonly router = inject(Router);

  protected signOut(): void {
    this.auth.logout().subscribe({
      // Navigate either way: if the call fails the local session is still cleared, and leaving the user on a
      // page they can no longer use would be worse than a redirect.
      next: () => void this.router.navigate(['/login']),
      error: () => {
        this.auth.clearSession();
        void this.router.navigate(['/login']);
      },
    });
  }
}
