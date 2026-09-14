import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppConfig } from '../config/app-config.service';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/** A stand-in for the session, letting a test decide what a refresh does. */
class FakeAuthService {
  token: string | null = 'first-token';
  restoreCalls = 0;
  restoreSucceeds = true;

  accessToken(): string | null {
    return this.token;
  }

  restore(): Promise<boolean> {
    this.restoreCalls += 1;

    if (this.restoreSucceeds) {
      this.token = 'second-token';
    }

    return Promise.resolve(this.restoreSucceeds);
  }
}

/**
 * Attaching the token, and renewing it when the server says it is stale.
 *
 * Access tokens last fifteen minutes. Without the 401 retry a user who left a tab open over lunch
 * meets a wall of failed requests until they reload — and the hub reconnects with the expired token
 * and never recovers. These pin the behaviour that makes a short lifetime survivable.
 */
describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: FakeAuthService;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    auth = new FakeAuthService();
    navigate = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: AppConfig, useValue: { apiBaseUrl: '' } },
        { provide: Router, useValue: { navigate, url: '/rooms' } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('attaches the bearer token to API requests', () => {
    http.get('/api/rooms').subscribe();

    const request = httpMock.expectOne('/api/rooms');

    expect(request.request.headers.get('Authorization')).toBe('Bearer first-token');
    request.flush([]);
    httpMock.verify();
  });

  it('leaves requests to other origins alone', () => {
    // A token must never travel to a third party that happens to be fetched through the same
    // HttpClient.
    http.get('https://example.test/data').subscribe();

    const request = httpMock.expectOne('https://example.test/data');

    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
    httpMock.verify();
  });

  it('renews the token and retries once when a request comes back 401', async () => {
    const results: unknown[] = [];
    http.get('/api/bookings/mine').subscribe((value) => results.push(value));

    httpMock
      .expectOne('/api/bookings/mine')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    // Let the refresh promise settle before the retry is issued.
    await Promise.resolve();
    await Promise.resolve();

    const retry = httpMock.expectOne('/api/bookings/mine');

    expect(retry.request.headers.get('Authorization')).toBe('Bearer second-token');
    retry.flush([{ id: 'b1' }]);

    expect(auth.restoreCalls).toBe(1);
    expect(results).toEqual([[{ id: 'b1' }]]);
    httpMock.verify();
  });

  it('does not retry a second time when the renewed token is also refused', async () => {
    // A second 401 is about authorisation, not expiry. Retrying again would loop.
    const errors: unknown[] = [];
    http.get('/api/rooms').subscribe({ error: (error) => errors.push(error) });

    httpMock.expectOne('/api/rooms').flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    await Promise.resolve();

    httpMock.expectOne('/api/rooms').flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();

    expect(auth.restoreCalls).toBe(1);
    expect(errors).toHaveLength(1);
    httpMock.verify();
  });

  it('sends the user to sign in when the refresh cookie is spent', async () => {
    auth.restoreSucceeds = false;

    const errors: unknown[] = [];
    http.get('/api/rooms').subscribe({ error: (error) => errors.push(error) });

    httpMock.expectOne('/api/rooms').flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    await Promise.resolve();

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/rooms' } });
    expect(errors).toHaveLength(1);
    httpMock.verify();
  });

  it('never tries to renew a session using the auth endpoints themselves', async () => {
    // A 401 from /api/auth/refresh means the session is genuinely over; refreshing in response to
    // it would recurse.
    const errors: unknown[] = [];
    http.post('/api/auth/refresh', {}).subscribe({ error: (error) => errors.push(error) });

    httpMock
      .expectOne('/api/auth/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    await Promise.resolve();

    expect(auth.restoreCalls).toBe(0);
    expect(errors).toHaveLength(1);
    httpMock.verify();
  });
});
