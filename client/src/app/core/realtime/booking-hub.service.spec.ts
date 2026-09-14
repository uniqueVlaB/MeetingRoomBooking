import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { AppConfig } from '../config/app-config.service';

/** Connections handed out by the mocked builder, in the order they were built. */
const built: FakeConnection[] = [];

/** Set by a test to make the next `start()` reject. */
let nextStartFails = false;

/**
 * The `accessTokenFactory` the service handed to the builder.
 *
 * Captured because SignalR calls it on every connect and reconnect, and what it returns at that
 * moment is the whole of the reconnect authentication story.
 */
let tokenFactory: (() => string | Promise<string>) | null = null;

/** The captured token factory, invoked. */
function capturedTokenFactory(): Promise<string> {
  if (!tokenFactory) {
    throw new Error('The service did not supply an accessTokenFactory.');
  }

  return Promise.resolve(tokenFactory());
}

/** A stand-in for a SignalR connection, recording what the service does with it. */
class FakeConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  readonly invocations: { method: string; args: unknown[] }[] = [];
  readonly handlers = new Map<string, (payload: unknown) => void>();
  startCount = 0;
  stopCount = 0;

  /** Resolves the pending `start()`, if one is waiting. */
  release: () => void = () => undefined;

  start(): Promise<void> {
    this.startCount += 1;

    if (nextStartFails) {
      nextStartFails = false;

      return Promise.reject(new Error('negotiate failed'));
    }

    // Stays pending until the test releases it, so two `watch()` calls genuinely overlap inside
    // the connect — which is the window the race lived in.
    return new Promise<void>((resolve) => {
      this.release = () => {
        this.state = HubConnectionState.Connected;
        resolve();
      };
    });
  }

  stop(): Promise<void> {
    this.stopCount += 1;
    this.state = HubConnectionState.Disconnected;

    return Promise.resolve();
  }

  on(method: string, handler: (payload: unknown) => void): void {
    this.handlers.set(method, handler);
  }

  invoke(method: string, ...args: unknown[]): Promise<void> {
    this.invocations.push({ method, args });

    if (this.nextInvokeFails) {
      this.nextInvokeFails = false;

      return Promise.reject(new Error('group join failed'));
    }

    return Promise.resolve();
  }

  /** Set by a test to make the next `invoke()` reject, standing in for a failed group rejoin. */
  nextInvokeFails = false;

  // The lifecycle handlers are captured rather than ignored: reconnect behaviour is the thing most
  // worth testing here, and it can only be driven by invoking them.
  reconnecting: () => void = () => undefined;
  reconnected: () => void = () => undefined;
  closed: () => void = () => undefined;

  onreconnecting(handler: () => void): void {
    this.reconnecting = handler;
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  onclose(handler: () => void): void {
    this.closed = handler;
  }
}

vi.mock('@microsoft/signalr', async () => {
  const actual = await vi.importActual<typeof import('@microsoft/signalr')>('@microsoft/signalr');

  class FakeBuilder {
    withUrl(_url: string, options?: { accessTokenFactory?: () => string | Promise<string> }): this {
      tokenFactory = options?.accessTokenFactory ?? null;

      return this;
    }

    withAutomaticReconnect(): this {
      return this;
    }

    configureLogging(): this {
      return this;
    }

    build(): FakeConnection {
      const connection = new FakeConnection();
      built.push(connection);

      return connection;
    }
  }

  return { ...actual, HubConnectionBuilder: FakeBuilder };
});

/**
 * Opening the live-update connection.
 *
 * The service is the only thing standing between the application and a duplicated SignalR
 * connection. Two `watch()` calls can overlap whenever a user steps through days or a route
 * changes, and before the in-flight promise was memoised both would build one: the second won the
 * field and the first stayed open with its handlers attached, so every broadcast arrived twice and
 * a connection leaked for the life of the page.
 */
describe('BookingHubService', () => {
  beforeEach(() => {
    built.length = 0;
    nextStartFails = false;
    tokenFactory = null;

    TestBed.configureTestingModule({
      providers: [
        { provide: AppConfig, useValue: { hubUrl: 'https://api.test/hubs/bookings' } },
        { provide: AuthService, useValue: { accessToken: () => 'token' } },
      ],
    });
  });

  it('opens one connection when two watches overlap', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    // Both start before either connect completes, which is the whole point.
    const first = hub.watch('room-1', '2026-09-14');
    const second = hub.watch('room-1', '2026-09-15');

    expect(built).toHaveLength(1);

    built[0].release();
    await Promise.all([first, second]);

    expect(built).toHaveLength(1);
    expect(built[0].startCount).toBe(1);
  });

  it('delivers a broadcast once, not once per attempted connection', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const received: unknown[] = [];
    hub.slotBooked$.subscribe((booking) => received.push(booking));

    const first = hub.watch('room-1', '2026-09-14');
    const second = hub.watch('room-1', '2026-09-14');

    built[0].release();
    await Promise.all([first, second]);

    // One connection means one registered handler; a leaked second would double this.
    built.forEach((connection) => connection.handlers.get('SlotBooked')?.({ id: 'b1' }));

    expect(received).toHaveLength(1);
  });

  it('joins the new group and leaves the old one when the date changes', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const first = hub.watch('room-1', '2026-09-14');
    built[0].release();
    await first;

    await hub.watch('room-1', '2026-09-15');

    expect(built[0].invocations).toEqual([
      { method: 'JoinRoomAsync', args: ['room-1', '2026-09-14'] },
      { method: 'LeaveRoomAsync', args: ['room-1', '2026-09-14'] },
      { method: 'JoinRoomAsync', args: ['room-1', '2026-09-15'] },
    ]);
  });

  it('leaves nothing behind when a connection fails to start', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    // A failed connection left in place would make the next watch() believe one already exists, and
    // the page would never recover its live updates.
    nextStartFails = true;
    await expect(hub.watch('room-1', '2026-09-14')).rejects.toThrow('negotiate failed');

    const retry = hub.watch('room-1', '2026-09-14');
    built[1].release();
    await retry;

    expect(built).toHaveLength(2);
    expect(built[1].invocations).toEqual([
      { method: 'JoinRoomAsync', args: ['room-1', '2026-09-14'] },
    ]);
  });

  it('reports a reconnect in progress rather than claiming to be live', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const watching = hub.watch('room-1', '2026-09-14');
    built[0].release();
    await watching;

    expect(hub.connectionState()).toBe('connected');

    built[0].reconnecting();

    // Nothing is arriving during the reconnect window, which can run to tens of seconds. Reporting
    // "live" through it is how a stale grid passes for a fresh one.
    expect(hub.connectionState()).toBe('reconnecting');
    expect(hub.isConnected()).toBe(false);
  });

  it('rejoins the group after a reconnect and asks subscribers to re-read', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const watching = hub.watch('room-1', '2026-09-14');
    built[0].release();
    await watching;

    let rejoins = 0;
    hub.rejoined$.subscribe(() => (rejoins += 1));

    built[0].invocations.length = 0;
    await built[0].reconnected();

    // Group membership does not survive a reconnect, so rejoining is mandatory...
    expect(built[0].invocations).toEqual([
      { method: 'JoinRoomAsync', args: ['room-1', '2026-09-14'] },
    ]);

    // ...and everything broadcast while the connection was down is gone, so subscribers are told
    // to re-read rather than left trusting what they are holding.
    expect(rejoins).toBe(1);
    expect(hub.connectionState()).toBe('connected');
  });

  it('stays offline when the group cannot be rejoined', async () => {
    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const watching = hub.watch('room-1', '2026-09-14');
    built[0].release();
    await watching;

    let rejoins = 0;
    hub.rejoined$.subscribe(() => (rejoins += 1));

    built[0].nextInvokeFails = true;
    await built[0].reconnected();

    // Connected to the hub but not back in the group means no updates will arrive. Reporting
    // "live" here would be the stale-schedule failure wearing a green light — and telling
    // subscribers to re-read would hand them a grid that then silently stops updating again.
    expect(hub.connectionState()).toBe('offline');
    expect(rejoins).toBe(0);
  });

  it('refreshes an expired access token instead of reconnecting without one', async () => {
    // A tab left open past the access token's lifetime has nothing to authenticate a reconnect
    // with, and the only other thing that renews it is an HTTP 401 in the interceptor. Without
    // this the connection fails every retry for a reason retrying cannot fix.
    let token: string | null = null;
    let restores = 0;

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AppConfig, useValue: { hubUrl: 'https://api.test/hubs/bookings' } },
        {
          provide: AuthService,
          useValue: {
            accessToken: () => token,
            restore: () => {
              restores += 1;
              token = 'refreshed-token';

              return Promise.resolve(true);
            },
          },
        },
      ],
    });

    const { BookingHubService } = await import('./booking-hub.service');
    const hub = TestBed.inject(BookingHubService);

    const watching = hub.watch('room-1', '2026-09-14');
    built[0].release();
    await watching;

    const supplied = await capturedTokenFactory();

    expect(restores).toBe(1);
    expect(supplied).toBe('refreshed-token');
  });
});
