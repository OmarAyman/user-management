import { Routes } from '@angular/router';

import { authGuard, roleGuard } from './core/guards/auth.guard';
import { RoleName } from './core/models/api.models';

/**
 * Feature areas are lazy-loaded: a read-only user never downloads the audit screen, and the sign-in page does
 * not carry the rest of the application with it.
 *
 * `title` carries a **translation key**, not text. `TranslatedTitleStrategy` resolves it, so the browser tab
 * follows the same localization rule as every string inside a template; the keys are the ones the pages
 * already use for their headings, so a title cannot drift from the page it names.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
    title: 'auth.login.title',
  },
  {
    path: '',
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'users' },
      {
        path: 'users',
        loadChildren: () => import('./features/users/users.routes').then((m) => m.userRoutes),
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/profile.page').then((m) => m.ProfilePage),
        title: 'profile.title',
      },
      {
        path: 'audit',
        // The route declares the role; the guard reads it. The server enforces the same rule, and a test
        // proves a non-Admin gets 403 from the API even though this guard would have stopped them first.
        canActivate: [roleGuard(RoleName.admin)],
        loadComponent: () => import('./features/audit/audit.page').then((m) => m.AuditPage),
        title: 'audit.title',
      },
      {
        path: 'forbidden',
        loadComponent: () => import('./features/errors/forbidden.page').then((m) => m.ForbiddenPage),
        title: 'forbidden.title',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
