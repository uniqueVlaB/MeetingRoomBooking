import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { AuthService } from '../../core/auth/auth.service';
import { Booking, Schedule, ScheduleSlot } from '../../core/models';
import { BookingHubService } from '../../core/realtime/booking-hub.service';

/** A message shown above the grid. */
interface Notice {
  kind: 'info' | 'error';
  text: string;
}

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
  private readonly rooms = inject(RoomsService);
  private readonly bookings = inject(BookingsService);
  private readonly hub = inject(BookingHubService);
  private readonly auth = inject(AuthService);

  /** Route parameter, bound by `withComponentInputBinding`. */
  readonly roomId = input.required<string>();

  /** The date being shown, as `yyyy-MM-dd`. */
  protected readonly date = signal(todayIso());

  /** The loaded schedule, or null before the first load. */
  protected readonly schedule = signal<Schedule | null>(null);

  /** Whether a load is in flight. */
  protected readonly loading = signal(false);

  /** The slot currently being booked or cancelled, so only that row shows a spinner. */
  protected readonly pendingSlotId = signal<string | null>(null);

  /** A message about the last action. */
  protected readonly notice = signal<Notice | null>(null);

  /** Whether live updates are currently flowing. */
  protected readonly isLive = this.hub.isConnected;

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
        // A hub that will not connect is not fatal: the schedule still loads and can be refreshed
        // by hand. Saying so is better than pretending the page is live when it is not.
        this.notice.set({
          kind: 'error',
          text: 'Live updates are unavailable; reload to see other people’s changes.',
        });
      });
    });

    this.hub.slotBooked$
      .pipe(takeUntilDestroyed())
      .subscribe((booking) => this.applyBooked(booking));

    this.hub.slotReleased$
      .pipe(takeUntilDestroyed())
      .subscribe((booking) => this.applyReleased(booking));
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
    return time.slice(0, 5);
  }

  /** Books a slot. */
  protected async book(slot: ScheduleSlot): Promise<void> {
    this.pendingSlotId.set(slot.timeSlotId);
    this.notice.set(null);

    try {
      await this.bookings.book({ timeSlotId: slot.timeSlotId, slotDate: this.date() });
      this.notice.set({
        kind: 'info',
        text: `Booked ${this.formatTime(slot.startTime)}–${this.formatTime(slot.endTime)}.`,
      });
    } catch (error) {
      // 409 is the expected outcome of losing a race, not a malfunction, so it gets a plain
      // explanation rather than an error dump.
      const conflict = error instanceof HttpErrorResponse && error.status === 409;

      this.notice.set({
        kind: 'error',
        text: conflict
          ? 'Somebody else booked that slot a moment before you. The schedule has been updated.'
          : describeError(error),
      });

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
      await this.bookings.cancel(slot.bookingId);
      this.notice.set({ kind: 'info', text: 'Booking cancelled; the slot is free again.' });
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
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

  /** Loads the schedule for a room and date. */
  private async load(roomId: string, date: string): Promise<void> {
    this.loading.set(true);

    try {
      this.schedule.set(await this.rooms.getSchedule(roomId, date));
    } catch (error) {
      this.schedule.set(null);
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.loading.set(false);
    }
  }

  /** Marks a slot as taken in response to a broadcast. */
  private applyBooked(booking: Booking): void {
    this.patchSlot(booking, (slot) => ({
      ...slot,
      isBooked: true,
      bookingId: booking.id,
      bookedByUserId: booking.userId,
      bookedByDisplayName: booking.userDisplayName,
    }));
  }

  /** Marks a slot as free in response to a broadcast. */
  private applyReleased(booking: Booking): void {
    this.patchSlot(booking, (slot) => ({
      ...slot,
      isBooked: false,
      bookingId: null,
      bookedByUserId: null,
      bookedByDisplayName: null,
    }));
  }

  /**
   * Applies a change to the slot a broadcast refers to.
   *
   * The room and date are checked even though the subscription is already scoped to them: a
   * broadcast can arrive in the moment between switching day and the group change taking effect,
   * and applying it would put a booking from Tuesday onto Monday's grid.
   */
  private patchSlot(booking: Booking, update: (slot: ScheduleSlot) => ScheduleSlot): void {
    const current = this.schedule();

    if (!current || booking.roomId !== current.roomId || booking.slotDate !== current.date) {
      return;
    }

    this.schedule.set({
      ...current,
      slots: current.slots.map((slot) =>
        slot.timeSlotId === booking.timeSlotId ? update(slot) : slot,
      ),
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

/** Turns an error into something worth showing a user. */
function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    // The API returns RFC 9457 problem details, whose "detail" is written for a human.
    return error.error?.detail ?? error.error?.title ?? `Request failed (${error.status}).`;
  }

  return 'Something went wrong. Please try again.';
}
