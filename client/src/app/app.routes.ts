import { Routes } from '@angular/router';
import { adminGuard, anonymousGuard, authGuard } from './core/auth/auth.guards';

/**
 * Application routes.
 *
 * Every feature is lazily loaded, so the initial bundle carries only the shell and whatever screen
 * the user actually asked for. Each carries a title, which `AppTitleStrategy` turns into the tab's
 * name -- worth having when the normal way to use this system is several tabs on one schedule.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'rooms' },
  {
    path: 'login',
    title: 'Sign in',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.LoginComponent),
  },
  {
    path: 'rooms',
    title: 'Rooms',
    canActivate: [authGuard],
    loadComponent: () => import('./features/rooms/rooms').then((m) => m.RoomsComponent),
  },
  {
    path: 'rooms/:roomId/schedule',
    title: 'Schedule',
    canActivate: [authGuard],
    loadComponent: () => import('./features/schedule/schedule').then((m) => m.ScheduleComponent),
  },
  {
    path: 'my-bookings',
    title: 'My bookings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/my-bookings/my-bookings').then((m) => m.MyBookingsComponent),
  },
  {
    path: 'admin',
    title: 'Administration',
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./features/admin/admin').then((m) => m.AdminComponent),
  },
  { path: '**', redirectTo: 'rooms' },
];
