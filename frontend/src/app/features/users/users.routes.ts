import { Routes } from '@angular/router';

import { roleGuard } from '../../core/guards/auth.guard';
import { RoleName } from '../../core/models/api.models';

/**
 * The create and edit routes are Admin-gated here as well as on the server. The guard keeps a non-Admin from
 * reaching a form whose every submission would be refused; the API's policy is what makes the refusal real.
 *
 * `title` values are translation keys, resolved by `TranslatedTitleStrategy`.
 */
export const userRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./users-list.page').then((m) => m.UsersListPage),
    title: 'users.list.title',
  },
  {
    path: 'new',
    canActivate: [roleGuard(RoleName.admin)],
    loadComponent: () => import('./user-form.page').then((m) => m.UserFormPage),
    title: 'users.form.createTitle',
  },
  {
    path: ':id/edit',
    canActivate: [roleGuard(RoleName.admin)],
    loadComponent: () => import('./user-form.page').then((m) => m.UserFormPage),
    title: 'users.form.editTitle',
  },
];
