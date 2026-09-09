import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Requires a signed-in user, attempting to resume a session first.
 *
 * The redirect target is `/login`, which is where the login route actually is. The reference
 * project redirected to a path its route table did not contain, so unauthenticated users landed on
 * the "not found" page instead of being asked to sign in.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    await auth.restore();
  }

  return (
    auth.isAuthenticated() ||
    // Remember where they were going, so signing in continues the journey rather than dumping
    // them on the home page.
    router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })
  );
};

/** Requires the administrator role. Assumes {@link authGuard} has already run. */
export const adminGuard: CanActivateFn = (_route, _state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAdmin() || router.createUrlTree(['/rooms']);
};

/** Keeps a signed-in user away from the login page. */
export const anonymousGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    await auth.restore();
  }

  return !auth.isAuthenticated() || router.createUrlTree(['/rooms']);
};
