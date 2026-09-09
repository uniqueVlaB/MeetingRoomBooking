import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
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

  private connection: HubConnection | null = null;
  private currentGroup: { roomId: string; date: string } | null = null;

  /** Emits when a slot becomes booked in the room and date currently being watched. */
  readonly slotBooked$ = this.slotBookedSubject.asObservable();

  /** Emits when a booking is cancelled in the room and date currently being watched. */
  readonly slotReleased$ = this.slotReleasedSubject.asObservable();

  /** Whether the hub connection is currently established. */
  readonly isConnected = signal(false);

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

  /** Stops watching and closes the connection. */
  async disconnect(): Promise<void> {
    await this.leaveCurrentGroup();

    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.isConnected.set(false);
    }
  }

  /** Opens the connection if it is not already open. */
  private async ensureConnected(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) {
      return this.connection;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(this.config.hubUrl, {
        // A browser WebSocket cannot set an Authorization header, so SignalR appends the token as
        // ?access_token=...; the API reads it there in its JwtBearerEvents.OnMessageReceived hook.
        accessTokenFactory: () => this.auth.accessToken() ?? '',
      })
      // Without this, a dropped connection leaves the schedule silently stale, which is worse than
      // an obviously broken page: the user would believe a taken slot is still free.
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on(SLOT_BOOKED, (booking: Booking) => this.slotBookedSubject.next(booking));
    connection.on(SLOT_RELEASED, (booking: Booking) => this.slotReleasedSubject.next(booking));

    connection.onreconnected(async () => {
      this.isConnected.set(true);

      // Group membership does not survive a reconnect, so rejoin explicitly. Easy to miss, and the
      // symptom -- updates simply stop arriving -- looks like nothing is wrong.
      if (this.currentGroup) {
        const { roomId, date } = this.currentGroup;
        await connection.invoke('JoinRoomAsync', roomId, date);
      }
    });

    connection.onclose(() => this.isConnected.set(false));

    await connection.start();
    this.connection = connection;
    this.isConnected.set(true);

    return connection;
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
