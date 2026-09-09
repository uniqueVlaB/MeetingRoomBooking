import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AppConfig } from '../config/app-config.service';
import { AuthService } from './auth.service';

/**
 * Attaches the bearer token to API requests.
 *
 * An interceptor rather than a header built at each call site: the reference project added the
 * header by hand in every service method, which is a rule that only holds until somebody forgets.
 *
 * Only requests to our own API are touched, so a token can never be attached to a third-party URL
 * that happens to be fetched through the same `HttpClient`.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).accessToken();
  const apiBaseUrl = inject(AppConfig).apiBaseUrl;

  const isApiRequest = apiBaseUrl
    ? request.url.startsWith(`${apiBaseUrl}/api/`)
    : request.url.startsWith('/api/');

  if (!token || !isApiRequest) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
