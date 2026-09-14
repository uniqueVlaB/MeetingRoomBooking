import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { AuthService } from '../../core/auth/auth.service';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { Booking, Schedule, ScheduleSlot } from '../../core/models';
import { BookingHubService } from '../../core/realtime/booking-hub.service';
import { Notice, failure, info } from '../../core/ui/notice';

/**
 * A room's schedule for one date: which slots are free, which are taken, and by whom.
 *
 * This is the screen the whole system exists for, and it carries both halves of the requirement.
 * Booking a slot can lose a race, in which case the API answers 409 and the user is told plainly
 * that somebody else got there first. And whether or not this user did anything, the grid updates
 * as other people book and cancel, because the component applies SignalR events to the loaded
 * schedule rather than waiting for a refresh.
 */
@Component({
  selector: 'app-schedule',
  imports: [FormsModule, RouterLink],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScheduleComponent {
  private readonly roomsApi = inject(RoomsService);
  private readonly bookingsApi = inject(BookingsService);
  private readonly hub = inject(BookingHubService);
  private readonly auth = inject(AuthService);

  /**
   * Identifies the most recent load.
   *
   * Loads are fired from an effect on the room and the date, so stepping quickly through days puts
   * several in flight at once and they can come back in any order. Without this an older response
   * can overwrite a newer one, leaving the grid showing a day the user has already left — and its
   * `finally` clears the spinner while the current day is still arriving.
   */
  private loadToken = 0;

  /** Route parameter, bound by `withComponentInputBinding`. */
  readonly roomId = input.required<string>();

  /** The date being shown, as `yyyy-MM-dd`. */
  protected readonly date = signal(todayIso());

  /** The loaded schedule, or null before the first load. */
  protected readonly schedule = signal<Schedule | null>(null);

  /** Whether a load is in flight. */
  protected readonly loading = signal(false);

  /**
   * Whether the last load failed.
   *
   * Tracked separately from the schedule being null, because the two mean opposite things to a
   * reader: "nothing came back" and "this room has no slots" look identical in the data and must
   * not look identical on screen. Without this the template answers a failed request with a
   * confident "This room has no bookable slots."
   */
  protected readonly loadFailed = signal(false);

  /** The slot currently being booked or cancelled, so only that row shows a spinner. */
  protected readonly pendingSlotId = signal<string | null>(null);

  /** A message about the last action. */
  protected readonly notice = signal<Notice | null>(null);

  /** Whether live updates are flowing, recovering, or stopped. */
  protected readonly connection = this.hub.connectionState;

  /** Slots in display order. */
  protected readonly slots = computed(() => this.schedule()?.slots ?? []);

  /** How many slots are still free, for the summary line. */
  protected readonly freeCount = computed(
    () => this.slots().filter((slot) => !slot.isBooked).length,
  );

  constructor() {
    // Reloads and re-subscribes whenever the room or the date changes. Both happen through signals,
    // so switching day is the same code path as arriving on the page.
    effect(() => {
      const roomId = this.roomId();
      const date = this.date();

      void this.load(roomId, date);
      void this.hub.watch(roomId, date).catch(() => {
        // A hub that will not connect is not fatal: the schedule still loads, this user's own
        // actions still show up, and the page can be refreshed by hand. Saying so is better than
        // pretending the page is live when it is not.
        this.notice.set(
          failure('Live updates are unavailable; reload to see other people’s changes.'),
        );
      });
    });

    this.hub.slotBooked$
      .pipe(takeUntilDestroyed())
      .subscribe((booking) => this.applyBooked(booking));

    this.hub.slotReleased$
      .pipe(takeUntilDestroyed())
      .subscribe((booking) => this.applyReleased(booking));

    // Rejoining the group restores the flow of updates but recovers none of the ones broadcast
    // while the connection was down. Whatever is on screen was accurate before the drop and may be
    // wrong now, so re-read it: the alternative is a grid that reports a taken slot as free under a
    // green "Live" badge, which is precisely the failure reconnecting is supposed to prevent.
    this.hub.rejoined$
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load(this.roomId(), this.date()));

    // Leaves the room+date group when this view goes away. The connection is shared and stays open,
    // but the server should not fan messages out to a browser with nothing left to render them.
    inject(DestroyRef).onDestroy(() => void this.hub.unwatch());
  }

  /** Reconnects after live updates have stopped, without reloading the page. */
  protected async reconnect(): Promise<void> {
    this.notice.set(null);

    try {
      await this.hub.watch(this.roomId(), this.date());

      // The connection was down for an unknown stretch, so the grid is suspect for the same reason
      // it is after an automatic reconnect.
      await this.load(this.roomId(), this.date());
    } catch (error) {
      this.notice.set(failure(describeError(error)));
    }
  }

  /** Reloads the schedule after a failed load. */
  protected async retry(): Promise<void> {
    this.notice.set(null);
    await this.load(this.roomId(), this.date());
  }

  /** Whether a slot is held by the signed-in user, who may therefore cancel it. */
  protected isMine(slot: ScheduleSlot): boolean {
    return slot.bookedByUserId !== null && slot.bookedByUserId === this.auth.session()?.userId;
  }

  /** Whether the signed-in user may cancel this slot's booking. */
  protected canCancel(slot: ScheduleSlot): boolean {
    return slot.isBooked && (this.isMine(slot) || this.auth.isAdmin());
  }

  /** Formats "09:00:00" as "09:00". */
  protected formatTime(time: string): string {
    return formatSlotTime(time);
  }

  /** Books a slot. */
  protected async book(slot: ScheduleSlot): Promise<void> {
    this.pendingSlotId.set(slot.timeSlotId);
    this.notice.set(null);

    try {
      const booking = await this.bookingsApi.book({
        timeSlotId: slot.timeSlotId,
        slotDate: this.date(),
      });

      // Apply the server's own answer rather than waiting for it to come back around over SignalR.
      // The broadcast usually arrives first and this is then a no-op, but when the hub is down --
      // which the effect above treats as survivable -- it is the only thing that marks the slot
      // taken. Without it a user books successfully, sees no change, and clicks again into a 409.
      this.applyBooked(booking);

      this.notice.set(
        info(`Booked ${this.formatTime(slot.startTime)}–${this.formatTime(slot.endTime)}.`),
      );
    } catch (error) {
      // 409 is the expected outcome of losing a race, not a malfunction, so it gets a plain
      // explanation rather than an error dump.
      const conflict = error instanceof HttpErrorResponse && error.status === 409;

      this.notice.set(
        failure(
          conflict
            ? 'Somebody else booked that slot a moment before you. The schedule has been updated.'
            : describeError(error),
        ),
      );

      if (conflict) {
        // Pull the truth from the server rather than guessing: the winning booking may not have
        // reached us over SignalR yet.
        await this.load(this.roomId(), this.date());
      }
    } finally {
      this.pendingSlotId.set(null);
    }
  }

  /** Cancels the booking holding a slot. */
  protected async cancel(slot: ScheduleSlot): Promise<void> {
    if (!slot.bookingId) {
      return;
    }

    this.pendingSlotId.set(slot.timeSlotId);
    this.notice.set(null);

    try {
      await this.bookingsApi.cancel(slot.bookingId);

      // As in book(): do not depend on the broadcast to show this user their own action.
      this.releaseSlot(slot.timeSlotId);

      this.notice.set(info('Booking cancelled; the slot is free again.'));
    } catch (error) {
      this.notice.set(failure(describeError(error)));
      await this.load(this.roomId(), this.date());
    } finally {
      this.pendingSlotId.set(null);
    }
  }

  /** Moves the view by a number of days. */
  protected shiftDate(days: number): void {
    const moved = new Date(`${this.date()}T00:00:00`);
    moved.setDate(moved.getDate() + days);
    this.date.set(toIso(moved));
  }

  /** Loads the schedule for a room and date, discarding a response newer work has superseded. */
  private async load(roomId: string, date: string): Promise<void> {
    const token = ++this.loadToken;

    this.loading.set(true);

    try {
      const schedule = await this.roomsApi.getSchedule(roomId, date);

      if (token === this.loadToken) {
        this.schedule.set(schedule);
        this.loadFailed.set(false);
      }
    } catch (error) {
      if (token === this.loadToken) {
        this.schedule.set(null);
        this.loadFailed.set(true);
        this.notice.set(failure(describeError(error)));
      }
    } finally {
      // Only the newest load owns the spinner; an older one finishing must not clear it while the
      // day the user is actually looking at is still on its way.
      if (token === this.loadToken) {
        this.loading.set(false);
      }
    }
  }

  /** Marks a slot as taken in response to a booking. */
  private applyBooked(booking: Booking): void {
    if (!this.describesCurrentView(booking)) {
      return;
    }

    this.patchSlot(booking.timeSlotId, (slot) => ({
      ...slot,
      isBooked: true,
      bookingId: booking.id,
      bookedByUserId: booking.userId,
      bookedByDisplayName: booking.userDisplayName,
    }));
  }

  /** Marks a slot as free in response to a cancellation. */
  private applyReleased(booking: Booking): void {
    if (!this.describesCurrentView(booking)) {
      return;
    }

    this.releaseSlot(booking.timeSlotId);
  }

  /** Marks one slot free by its identifier. */
  private releaseSlot(timeSlotId: string): void {
    this.patchSlot(timeSlotId, (slot) => ({
      ...slot,
      isBooked: false,
      bookingId: null,
      bookedByUserId: null,
      bookedByDisplayName: null,
    }));
  }

  /**
   * Whether a booking belongs to the room and date on screen.
   *
   * Checked even though the subscription is already scoped to them: a broadcast can arrive in the
   * moment between switching day and the group change taking effect, and applying it would put a
   * booking from Tuesday onto Monday's grid.
   */
  private describesCurrentView(booking: Booking): boolean {
    const current = this.schedule();

    return (
      current !== null && booking.roomId === current.roomId && booking.slotDate === current.date
    );
  }

  /** Applies a change to one slot of the loaded schedule. */
  private patchSlot(timeSlotId: string, update: (slot: ScheduleSlot) => ScheduleSlot): void {
    const current = this.schedule();

    if (!current) {
      return;
    }

    this.schedule.set({
      ...current,
      slots: current.slots.map((slot) => (slot.timeSlotId === timeSlotId ? update(slot) : slot)),
    });
  }
}

/** Today as `yyyy-MM-dd` in the browser's local time zone. */
function todayIso(): string {
  return toIso(new Date());
}

/** Formats a date as `yyyy-MM-dd`, avoiding the UTC shift that `toISOString` would introduce. */
function toIso(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');

  return `${value.getFullYear()}-${month}-${day}`;
}
