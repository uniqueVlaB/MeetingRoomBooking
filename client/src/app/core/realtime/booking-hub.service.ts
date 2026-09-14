import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  RetryContext,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AppConfig } from '../config/app-config.service';
import { Booking } from '../models';

/** Server-to-client method invoked when a slot becomes booked. Matches `BookingHub`. */
const SLOT_BOOKED = 'SlotBooked';

/** Server-to-client method invoked when a cancellation releases a slot. */
const SLOT_RELEASED = 'SlotReleased';

/**
 * How long to wait before each reconnect attempt, in milliseconds.
 *
 * SignalR's default schedule gives up after roughly 42 seconds, which is far too soon for a screen
 * somebody leaves open all day: a laptop that sleeps through lunch would wake to a dead connection
 * and no way back except a browser reload. This backs off to half a minute and then keeps trying
 * indefinitely, because a schedule nobody is watching costs the server nothing.
 */
const RETRY_DELAYS = [0, 2_000, 5_000, 10_000, 30_000];

/** Whether live updates are flowing, trying to recover, or given up. */
export type ConnectionState = 'connected' | 'reconnecting' | 'offline';

/**
 * Live schedule updates over SignalR.
 *
 * Subscription is per room *and* date, matching the server's group naming, so a browser showing
 * Monday is not woken by a change to Tuesday. The connection is shared by the whole application:
 * opening one per schedule view would multiply connections against the Azure SignalR quota for no
 * benefit.
 */
@Injectable({ providedIn: 'root' })
export class BookingHubService {
  private readonly auth = inject(AuthService);
  private readonly config = inject(AppConfig);

  private readonly slotBookedSubject = new Subject<Booking>();
  private readonly slotReleasedSubject = new Subject<Booking>();
  private readonly rejoinedSubject = new Subject<void>();

  private readonly state = signal<ConnectionState>('offline');

  private connection: HubConnection | null = null;
  private connecting: Promise<HubConnection> | null = null;
  private currentGroup: { roomId: string; date: string } | null = null;

  /** Emits when a slot becomes booked in the room and date currently being watched. */
  readonly slotBooked$ = this.slotBookedSubject.asObservable();

  /** Emits when a booking is cancelled in the room and date currently being watched. */
  readonly slotReleased$ = this.slotReleasedSubject.asObservable();

  /**
   * Emits after the connection has dropped and successfully rejoined its group.
   *
   * Rejoining restores the *flow* of updates but says nothing about the ones that were broadcast
   * while the connection was down — those are gone, and a schedule that silently keeps showing
   * pre-disconnect state is the exact failure automatic reconnect exists to avoid. Subscribers are
   * expected to re-read from the server rather than trust what they are holding.
   */
  readonly rejoined$ = this.rejoinedSubject.asObservable();

  /** Whether live updates are flowing, recovering, or stopped. */
  readonly connectionState = this.state.asReadonly();

  /** Whether the hub connection is currently established. */
  readonly isConnected = computed(() => this.state() === 'connected');

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.disconnect());
  }

  /**
   * Watches one room on one date, leaving any previously watched group first.
   *
   * @param roomId The room to watch.
   * @param date The date as `yyyy-MM-dd`.
   */
  async watch(roomId: string, date: string): Promise<void> {
    const connection = await this.ensureConnected();
    await this.leaveCurrentGroup();

    await connection.invoke('JoinRoomAsync', roomId, date);
    this.currentGroup = { roomId, date };
  }

  /**
   * Stops watching the current group without closing the connection.
   *
   * Called when a schedule view is destroyed. The connection is deliberately left open — it is
   * shared, and the next view will want it — but staying in a group nobody is rendering means the
   * server fans messages out to a browser that will discard them.
   */
  async unwatch(): Promise<void> {
    await this.leaveCurrentGroup();
  }

  /** Stops watching and closes the connection. */
  async disconnect(): Promise<void> {
    await this.leaveCurrentGroup();

    if (this.connection) {
      const connection = this.connection;

      // Cleared first, so a watch() racing this teardown opens a fresh connection rather than
      // handing back the one being stopped.
      this.connection = null;
      this.state.set('offline');

      await connection.stop();
    }
  }

  /**
   * Opens the connection if it is not already open.
   *
   * The in-flight promise is memoised, exactly as `AuthService.restore()` memoises its refresh.
   * Without it two overlapping `watch()` calls — stepping through days, or a route change — both
   * see a connection that is not yet established and both build one. The second wins the field, and
   * the first stays open with its handlers still attached: every broadcast then arrives twice, and
   * an Azure SignalR connection is held for the life of the page with nothing referencing it.
   */
  private ensureConnected(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) {
      return Promise.resolve(this.connection);
    }

    this.connecting ??= this.openConnection().finally(() => {
      this.connecting = null;
    });

    return this.connecting;
  }

  /** Builds and starts a connection. Only ever called through {@link ensureConnected}. */
  private async openConnection(): Promise<HubConnection> {
    const connection = new HubConnectionBuilder()
      .withUrl(this.config.hubUrl, {
        // A browser WebSocket cannot set an Authorization header, so SignalR appends the token as
        // ?access_token=...; the API reads it there in its JwtBearerEvents.OnMessageReceived hook.
        accessTokenFactory: () => this.currentAccessToken(),
      })
      // Without this, a dropped connection leaves the schedule silently stale, which is worse than
      // an obviously broken page: the user would believe a taken slot is still free.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context: RetryContext) =>
          RETRY_DELAYS[Math.min(context.previousRetryCount, RETRY_DELAYS.length - 1)],
      })
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on(SLOT_BOOKED, (booking: Booking) => this.slotBookedSubject.next(booking));
    connection.on(SLOT_RELEASED, (booking: Booking) => this.slotReleasedSubject.next(booking));

    // Reported separately from "offline": updates are not arriving, but they are expected back, and
    // telling the user "Live" throughout a reconnect is how a stale grid passes for a fresh one.
    connection.onreconnecting(() => this.state.set('reconnecting'));

    connection.onreconnected(() => void this.rejoinAfterReconnect(connection));

    connection.onclose(() => this.state.set('offline'));

    try {
      await connection.start();
    } catch (error) {
      // Leave nothing half-built behind: a failed connection that stayed in `this.connection` would
      // make the next ensureConnected() believe one already exists.
      await connection.stop().catch(() => undefined);
      this.state.set('offline');
      throw error;
    }

    this.connection = connection;
    this.state.set('connected');

    return connection;
  }

  /**
   * Rejoins the watched group after a reconnect and tells subscribers to re-read.
   *
   * Group membership does not survive a reconnect, so rejoining is not optional — and the symptom
   * of forgetting is that updates simply stop arriving, which looks like nothing is wrong.
   */
  private async rejoinAfterReconnect(connection: HubConnection): Promise<void> {
    if (!this.currentGroup) {
      this.state.set('connected');
      return;
    }

    const { roomId, date } = this.currentGroup;

    try {
      await connection.invoke('JoinRoomAsync', roomId, date);
      this.state.set('connected');

      // Only after the group is rejoined, so a subscriber's refetch cannot land in the window where
      // it would miss a booking made a moment later and then never hear about it.
      this.rejoinedSubject.next();
    } catch {
      // Reconnected to the hub but not back in the group, so no updates will arrive. Reporting
      // "live" here would be the stale-schedule failure wearing a green light.
      this.state.set('offline');
    }
  }

  /**
   * The token to authenticate the connection with, refreshing it first when there is none.
   *
   * Access tokens are short-lived and the only other thing that renews them is an HTTP 401 in the
   * interceptor. A tab left open past expiry therefore has no valid token when it tries to
   * reconnect, and would fail every attempt for a reason no amount of retrying can fix.
   * `AuthService.restore()` memoises its refresh, so racing the interceptor cannot redeem the
   * rotating refresh token twice.
   */
  private async currentAccessToken(): Promise<string> {
    const held = this.auth.accessToken();

    if (held) {
      return held;
    }

    await this.auth.restore();

    return this.auth.accessToken() ?? '';
  }

  /** Leaves the group currently being watched, if any. */
  private async leaveCurrentGroup(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected && this.currentGroup) {
      const { roomId, date } = this.currentGroup;
      await this.connection.invoke('LeaveRoomAsync', roomId, date);
    }

    this.currentGroup = null;
  }
}
