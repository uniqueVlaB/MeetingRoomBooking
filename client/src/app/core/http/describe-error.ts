import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns a failed request into something worth showing a user.
 *
 * One implementation for the whole application. Three copies of this used to drift, and two screens
 * skipped it entirely and showed a canned string — so whether you were told *why* a booking failed
 * depended on which page you were standing on.
 *
 * The API answers every error with RFC 9457 problem details, whose `detail` is written for a human,
 * so that is what a user sees when it is there.
 */
export function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { detail?: string; title?: string } | string | null;

    if (body && typeof body === 'object') {
      return body.detail ?? body.title ?? fallbackFor(error.status);
    }

    return fallbackFor(error.status);
  }

  return 'Something went wrong. Please try again.';
}

/** A plain sentence for a status code that arrived without problem details. */
function fallbackFor(status: number): string {
  // Status 0 is the browser refusing to say why — offline, CORS, a blocked request. "Request failed
  // (0)" tells a user nothing they can act on.
  if (status === 0) {
    return 'The server could not be reached. Check your connection and try again.';
  }

  return `Request failed (${status}).`;
}
