import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AppConfig } from '../config/app-config.service';
import { AuthService } from './auth.service';

/**
 * Attaches the bearer token to API requests, and renews it once when the server says it is stale.
 *
 * An interceptor rather than a header built at each call site: attaching it by hand in every
 * service method is a rule that only holds until somebody forgets.
 *
 * Only requests to our own API are touched, so a token can never be attached to a third-party URL
 * that happens to be fetched through the same `HttpClient`.
 *
 * The 401 retry is what makes a short access-token lifetime usable. Tokens last fifteen minutes;
 * without this, a user who left the tab open over lunch meets a wall of failed requests until they
 * reload, and the hub reconnects with an expired token and never recovers. `AuthService.restore()`
 * memoises the refresh, so a page that fires six requests at once performs one renewal and not six
 * competing rotations.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const apiBaseUrl = inject(AppConfig).apiBaseUrl;

  if (!isApiRequest(request, apiBaseUrl)) {
    return next(request);
  }

  // The auth endpoints are how a session is established or renewed, so they must never trigger the
  // renewal below: a 401 from /api/auth/refresh means the session is genuinely over.
  const isAuthEndpoint = request.url.includes('/api/auth/');

  return next(withToken(request, auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isAuthEndpoint) {
        return throwError(() => error);
      }

      return from(auth.restore()).pipe(
        switchMap((restored) => {
          if (!restored) {
            // The refresh cookie is gone or spent. Send them to sign in rather than surfacing a
            // bare 401 on whichever screen happened to be open.
            void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });

            return throwError(() => error);
          }

          // Once only: this retry carries a token that was just minted, so a second 401 is about
          // authorisation, not expiry, and retrying again would loop.
          return next(withToken(request, auth.accessToken()));
        }),
      );
    }),
  );
};

/** Whether a request is bound for our own API. */
function isApiRequest(request: HttpRequest<unknown>, apiBaseUrl: string): boolean {
  return apiBaseUrl
    ? request.url.startsWith(`${apiBaseUrl}/api/`)
    : request.url.startsWith('/api/');
}

/** Clones a request with the bearer token attached, if there is one. */
function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
