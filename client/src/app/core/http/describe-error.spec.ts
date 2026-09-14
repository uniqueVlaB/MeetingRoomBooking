import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './describe-error';

/**
 * What a user is told when a request fails.
 *
 * This is the only place the application turns a failure into a sentence, and every screen shows
 * whatever it returns — so "somebody else took that slot" reaching the user intact is this
 * function's responsibility.
 */
describe('describeError', () => {
  it('prefers the problem-details explanation written for a human', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: { title: 'Conflict', detail: 'That slot has just been booked by somebody else.' },
    });

    expect(describeError(error)).toBe('That slot has just been booked by somebody else.');
  });

  it('falls back to the title when there is no detail', () => {
    const error = new HttpErrorResponse({ status: 404, error: { title: 'Not found' } });

    expect(describeError(error)).toBe('Not found');
  });

  it('names the status when the body carries no problem details', () => {
    const error = new HttpErrorResponse({ status: 503, error: null });

    expect(describeError(error)).toBe('Request failed (503).');
  });

  it('survives a body that is a string rather than problem details', () => {
    // A proxy or gateway can answer with HTML, and reading `.detail` off a string yields undefined
    // rather than throwing — but only because of the typeof guard.
    const error = new HttpErrorResponse({ status: 502, error: '<html>Bad gateway</html>' });

    expect(describeError(error)).toBe('Request failed (502).');
  });

  it('explains status 0 rather than showing it', () => {
    // Status 0 is the browser declining to say why: offline, CORS, a blocked request. "Request
    // failed (0)" gives a user nothing to act on.
    const error = new HttpErrorResponse({ status: 0, error: null });

    expect(describeError(error)).toBe(
      'The server could not be reached. Check your connection and try again.',
    );
  });

  it('handles something that is not an HTTP error at all', () => {
    expect(describeError(new TypeError('undefined is not a function'))).toBe(
      'Something went wrong. Please try again.',
    );
  });
});
