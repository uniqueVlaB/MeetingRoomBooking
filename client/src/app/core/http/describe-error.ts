import { HttpErrorResponse } from '@angular/common/http';

/** The subset of `TranslationService.t()` this module needs, so it stays free of an Angular DI. */
export type ErrorTranslator = (
  key: 'errors.offline' | 'errors.requestFailed' | 'errors.generic',
  params?: Record<string, string | number>,
) => string;

/** English, used when a caller has no `TranslationService` in scope — a spec, chiefly. */
const defaultTranslate: ErrorTranslator = (key, params) => {
  switch (key) {
    case 'errors.offline':
      return 'The server could not be reached. Check your connection and try again.';
    case 'errors.requestFailed':
      return `Request failed (${params?.['status']}).`;
    case 'errors.generic':
      return 'Something went wrong. Please try again.';
  }
};

/**
 * Turns a failed request into something worth showing a user.
 *
 * One implementation for the whole application. Three copies of this used to drift, and two screens
 * skipped it entirely and showed a canned string — so whether you were told *why* a booking failed
 * depended on which page you were standing on.
 *
 * The API answers every error with RFC 9457 problem details, whose `detail` is written for a human,
 * so that is what a user sees when it is there — in whatever language the API itself answers in,
 * since translating a server's own prose would need the server to know the client's chosen language
 * too. `t` only translates the two sentences this function makes up itself, when there is no
 * `detail` to fall back to; every call site passes its `TranslationService.t`, bound to the current
 * locale, so those two sentences follow the language switch like everything else in the shell.
 */
export function describeError(error: unknown, t: ErrorTranslator = defaultTranslate): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { detail?: string; title?: string } | string | null;

    if (body && typeof body === 'object') {
      return body.detail ?? body.title ?? fallbackFor(error.status, t);
    }

    return fallbackFor(error.status, t);
  }

  return t('errors.generic');
}

/** A plain sentence for a status code that arrived without problem details. */
function fallbackFor(status: number, t: ErrorTranslator): string {
  // Status 0 is the browser refusing to say why — offline, CORS, a blocked request. "Request failed
  // (0)" tells a user nothing they can act on.
  if (status === 0) {
    return t('errors.offline');
  }

  return t('errors.requestFailed', { status });
}
