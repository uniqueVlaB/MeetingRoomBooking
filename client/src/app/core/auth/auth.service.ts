import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config.service';
import { AuthSession, LoginRequest, ROLE_ADMIN, RegisterRequest } from '../models';

/**
 * Holds the signed-in session.
 *
 * The access token is kept in memory only — never in `localStorage`, where any successful XSS could
 * read it. Surviving a page reload is the refresh token's job: it lives in an HttpOnly cookie the
 * browser sends automatically, and `restore()` exchanges it for a fresh access token at start-up.
 *
 * The token is exposed through a getter because two very different consumers need it: the HTTP
 * interceptor, and the SignalR client, which must pass it as a query parameter rather than a header.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private readonly sessionSignal = signal<AuthSession | null>(null);
  private restoreInFlight: Promise<boolean> | null = null;

  /** The current session, or null when signed out. */
  readonly session = this.sessionSignal.asReadonly();

  /** Whether somebody is signed in. */
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);

  /** Whether the signed-in user may manage rooms and see all bookings. */
  readonly isAdmin = computed(() => this.sessionSignal()?.roles.includes(ROLE_ADMIN) ?? false);

  /** The signed-in user's display name, for the header. */
  readonly displayName = computed(() => this.sessionSignal()?.displayName ?? '');

  /** The raw access token, for the interceptor and the SignalR connection. */
  accessToken(): string | null {
    return this.sessionSignal()?.accessToken ?? null;
  }

  /**
   * Attempts to resume a session from the refresh cookie.
   *
   * Called at start-up, by the route guards, and by the HTTP interceptor when a request comes back
   * 401. The in-flight promise is memoised so that several callers resolving at once produce one
   * request rather than a burst — and, because refresh tokens rotate on use and the server now
   * refuses a second redemption of the same one, a burst would invalidate its own tokens and sign
   * the user out.
   *
   * @returns Whether a session is held afterwards.
   */
  restore(): Promise<boolean> {
    this.restoreInFlight ??= this.refresh().finally(() => {
      this.restoreInFlight = null;
    });

    return this.restoreInFlight;
  }

  /** Creates an account and signs in. */
  async register(request: RegisterRequest): Promise<void> {
    this.sessionSignal.set(await this.post<AuthSession>('register', request));
  }

  /** Signs in with an email and password. */
  async login(request: LoginRequest): Promise<void> {
    this.sessionSignal.set(await this.post<AuthSession>('login', request));
  }

  /** Signs out, revoking the refresh token server-side. */
  async logout(): Promise<void> {
    try {
      await this.post<void>('logout', {});
    } finally {
      // Clear locally even if the call failed; the user asked to be signed out.
      this.sessionSignal.set(null);
    }
  }

  /** Exchanges the refresh cookie for a new access token. */
  private async refresh(): Promise<boolean> {
    try {
      this.sessionSignal.set(await this.post<AuthSession>('refresh', {}));
      return true;
    } catch {
      // No cookie, or an expired one. Being signed out is the correct outcome, not an error.
      this.sessionSignal.set(null);
      return false;
    }
  }

  /**
   * Posts to an auth endpoint.
   *
   * `withCredentials` is essential: the refresh cookie is set on the API's origin, which is a
   * different Web App from the client in Azure, so the browser only sends and stores it when the
   * request explicitly asks for credentials.
   */
  private post<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(
      this.http.post<T>(`${this.config.apiBaseUrl}/api/auth/${path}`, body, {
        withCredentials: true,
      }),
    );
  }
}
