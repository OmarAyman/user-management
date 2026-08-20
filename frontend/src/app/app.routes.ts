import { Routes } from '@angular/router';

import { authGuard, roleGuard } from './core/guards/auth.guard';
import { RoleName } from './core/models/api.models';

/**
 * Feature areas are lazy-loaded: a read-only user never downloads the audit screen, and the sign-in page does
 * not carry the rest of the application with it.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
    title: 'Sign in',
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
        title: 'My profile',
      },
      {
        path: 'audit',
        // The route declares the role; the guard reads it. The server enforces the same rule, and a test
        // proves a non-Admin gets 403 from the API even though this guard would have stopped them first.
        canActivate: [roleGuard(RoleName.admin)],
        loadComponent: () => import('./features/audit/audit.page').then((m) => m.AuditPage),
        title: 'Audit trail',
      },
      {
        path: 'forbidden',
        loadComponent: () => import('./features/errors/forbidden.page').then((m) => m.ForbiddenPage),
        title: 'Not permitted',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
