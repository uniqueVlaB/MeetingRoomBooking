import { Routes } from '@angular/router';
import { adminGuard, anonymousGuard, authGuard } from './core/auth/auth.guards';

/**
 * Application routes.
 *
 * Every feature is lazily loaded, so the initial bundle carries only the shell and whatever screen
 * the user actually asked for. Each carries a title, which `AppTitleStrategy` turns into the tab's
 * name -- worth having when the normal way to use this system is several tabs on one schedule.
 *
 * The title itself is a `TranslationKey` (see `core/i18n/translations.ts`), not display text:
 * `AppTitleStrategy` translates it, so the tab's name follows the language switch the same way the
 * page underneath it does.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'rooms' },
  {
    path: 'login',
    title: 'routes.signIn',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.LoginComponent),
  },
  {
    path: 'rooms',
    title: 'routes.rooms',
    canActivate: [authGuard],
    loadComponent: () => import('./features/rooms/rooms').then((m) => m.RoomsComponent),
  },
  {
    path: 'rooms/:roomId/schedule',
    title: 'routes.schedule',
    canActivate: [authGuard],
    loadComponent: () => import('./features/schedule/schedule').then((m) => m.ScheduleComponent),
  },
  {
    path: 'my-bookings',
    title: 'routes.myBookings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/my-bookings/my-bookings').then((m) => m.MyBookingsComponent),
  },
  {
    path: 'admin',
    title: 'routes.admin',
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./features/admin/admin').then((m) => m.AdminComponent),
  },
  { path: '**', redirectTo: 'rooms' },
];
